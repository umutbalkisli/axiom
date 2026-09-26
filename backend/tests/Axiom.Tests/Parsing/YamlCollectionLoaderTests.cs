using Axiom.Parsing;

namespace Axiom.Tests.Parsing;

public class YamlCollectionLoaderTests
{
    private static LoadedCollection Load(TempFolder folder) => new YamlCollectionLoader().Load(folder.Path);

    private const string MinimalCollection = "name: C\nvariables:\n  base_url: http://x\n";

    [Fact]
    public void Loads_settings_tests_and_shared_steps()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", """
            name: Todos
            description: demo
            variables: { base_url: "http://x", page: 2 }
            connections:
              db: { provider: sqlite, connection_string: "Data Source=data/x.db" }
            secrets:
              token:
                provider: env
                key: TOKEN
                environments:
                  ci: { provider: k8s, key: "a/b" }
            run_settings: { max_parallel_test_cases: 2, step_timeout_seconds: 5 }
            request_defaults:
              headers: { Accept: application/json }
            """);
        folder.Write("tests/b.test.yaml", "name: B\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}' }\n");
        folder.Write("tests/a.test.yaml", "name: A\nvariables: { local_var: 1 }\nsteps: []\n");
        folder.Write("shared/login.shared.yaml", "name: Login\nrun: once\nsteps:\n- { id: s, type: request, method: POST, url: x, save_as: session }\n");

        var loaded = Load(folder);

        Assert.Equal("Todos", loaded.Collection.Name);
        Assert.Equal(2, loaded.Collection.RunSettings.MaxParallelTestCases);
        Assert.Equal(5, loaded.Collection.RunSettings.StepTimeoutSeconds);
        Assert.Equal("application/json", loaded.Collection.RequestDefaults.Headers["Accept"]);
        Assert.Equal("k8s", loaded.Collection.Secrets["token"].Environments!["ci"].Provider);
        Assert.Equal(["A", "B"], loaded.TestCases.Select(t => t.Name));          // ordered by file name
        Assert.Equal("1", loaded.TestCases[0].Variables["local_var"]?.ToString());
        Assert.Equal("Login", loaded.SharedSteps["login"].Name);
        Assert.True(loaded.SharedSteps["LOGIN"].RunsOnce);                       // lookups are case-insensitive
        Assert.Equal("s", loaded.SharedSteps["login"].Steps[0].Id);
    }

    [Fact]
    public void Defaults_apply_when_settings_are_missing_or_invalid()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\nrun_settings: { max_parallel_test_cases: 0, step_timeout_seconds: -1 }\n");

        var settings = Load(folder).Collection.RunSettings;

        Assert.Equal(4, settings.MaxParallelTestCases);
        Assert.Equal(30, settings.StepTimeoutSeconds);
    }

    [Fact]
    public void Steps_get_default_names_and_ids()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write("tests/a.test.yaml", "name: A\nsteps:\n- { type: request, method: GET, url: x }\n- { id: named, type: ' request ', method: GET, url: x }\n");

        var steps = Load(folder).TestCases[0].Steps;

        Assert.False(string.IsNullOrEmpty(steps[0].Id));
        Assert.Equal(steps[0].Id, steps[0].Name);
        Assert.Equal("named", steps[1].Name);
        Assert.Equal("request", steps[1].Type);            // type is trimmed
    }

    [Fact]
    public void Relative_sqlite_paths_are_resolved_against_the_collection_folder_but_templated_ones_are_left_alone()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", """
            name: C
            connections:
              rel: { provider: sqlite, connection_string: "Data Source=data/x.db" }
              tpl: { provider: sqlite, connection_string: "Data Source={{secret.db_path}}" }
              sql: { provider: sqlserver, connection_string: "Server=db;Database=d" }
            """);

        var connections = Load(folder).Collection.Connections;

        Assert.Contains(Path.Combine(folder.Path, "data", "x.db"), connections["rel"].ConnectionString);
        Assert.Equal("Data Source={{secret.db_path}}", connections["tpl"].ConnectionString);
        Assert.Equal("Server=db;Database=d", connections["sql"].ConnectionString);
    }

    [Fact]
    public void A_folder_without_a_collection_file_is_an_error()
    {
        using var folder = new TempFolder();
        Assert.Throws<FileNotFoundException>(() => Load(folder));
    }

    [Theory]
    [InlineData("collection.yaml", "name: C\nvariables:\n  bad2: 1\n", "bad2")]
    [InlineData("collection.yaml", "name: C\nvariables:\n  secret: 1\n", "reserved")]
    [InlineData("tests/a.test.yaml", "name: A\nvariables: { my-var: 1 }\nsteps: []\n", "my-var")]
    [InlineData("tests/a.test.yaml", "name: A\nsteps:\n- { id: r, type: request, method: GET, url: x, save_as: 'resp.data' }\n", "resp.data")]
    [InlineData("shared/s.shared.yaml", "name: S\nsteps:\n- { id: r, type: request, method: GET, url: x, save_as: 'a-b' }\n", "a-b")]
    public void Invalid_variable_names_are_rejected_with_the_file_and_the_name(string file, string content, string expectedInMessage)
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write(file, content);

        var ex = Assert.Throws<InvalidOperationException>(() => Load(folder));

        Assert.Contains(expectedInMessage, ex.Message);
        Assert.Contains(Path.GetFileName(file), ex.Message);
    }

    [Fact]
    public void Include_steps_must_point_at_existing_shared_steps()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write("tests/a.test.yaml", "name: A\nsteps:\n- { id: i, type: include, ref: missing }\n");

        var ex = Assert.Throws<InvalidOperationException>(() => Load(folder));

        Assert.Contains("missing", ex.Message);
        Assert.Contains("do not exist", ex.Message);
    }

    [Fact]
    public void Shared_steps_may_include_other_shared_steps_and_tests_in_subfolders_are_found()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write("shared/a.shared.yaml", "name: A\nsteps:\n- { id: i, type: include, ref: b }\n");
        folder.Write("shared/b.shared.yaml", "name: B\nsteps: []\n");
        folder.Write("tests/nested/deep.test.yaml", "name: Deep\nsteps: []\n");

        var loaded = Load(folder);

        Assert.Equal(2, loaded.SharedSteps.Count);
        Assert.Equal("Deep", Assert.Single(loaded.TestCases).Name);
    }
}
