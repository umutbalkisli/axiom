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

    [Fact]
    public void A_misspelled_key_fails_the_load_with_file_line_and_the_likely_intended_key()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        var path = folder.Write("tests/a.test.yaml", """
            name: A
            steps:
            - id: r
              type: request
              method: GET
              url: '{{base_url}}'
              asert:
              - { source: status, operator: '==', expected: 200 }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => Load(folder));

        Assert.Contains(path, error.Message);
        Assert.Contains("line 7", error.Message);
        Assert.Contains("unknown key 'asert' in a step", error.Message);
        Assert.Contains("Did you mean 'assert'?", error.Message);
    }

    [Theory]
    [InlineData("collection.yaml", "name: C\nrun_setings: { max_parallel_test_cases: 2 }\n", "in the collection", "run_settings")]
    [InlineData("collection.yaml", "name: C\nrun_settings: { max_paralel_test_cases: 2 }\n", "in run_settings", "max_parallel_test_cases")]
    [InlineData("tests/a.test.yaml", "name: A\nstep: []\n", "in the test", "steps")]
    [InlineData("tests/a.test.yaml", "name: A\nsteps:\n- { id: r, type: request, url: x, assert: [ { source: status, operater: '==' } ] }\n", "in an assertion", "operator")]
    [InlineData("shared/s.shared.yaml", "name: S\nrun_mode: once\nsteps: []\n", "in the shared steps", null)]
    public void Unknown_keys_are_rejected_in_every_kind_of_file(string file, string yaml, string section, string? suggestion)
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write(file, yaml);

        var error = Assert.Throws<InvalidOperationException>(() => Load(folder));

        Assert.Contains(section, error.Message);
        if (suggestion is not null)
        {
            Assert.Contains($"Did you mean '{suggestion}'?", error.Message);
        }
    }

    [Fact]
    public void A_file_the_loader_cannot_read_names_the_file_and_position()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        var path = folder.Write("tests/a.test.yaml", "name: A\nsteps: [ { id: r\n");

        var error = Assert.Throws<InvalidOperationException>(() => Load(folder));

        Assert.StartsWith($"{path} (line ", error.Message);
    }

    [Fact]
    public void Keys_the_desktop_app_writes_are_accepted()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write("tests/a.test.yaml", "name: A\ndescription: d\nendpoint: /todos/{id}\nmethod: GET\nvariables: {}\nsteps: []\n");

        var test = Assert.Single(Load(folder).TestCases);

        Assert.Equal("/todos/{id}", test.Endpoint);
        Assert.Equal("GET", test.Method);
    }

    [Fact]
    public void A_filter_loads_only_the_picked_tests_and_does_not_parse_the_others()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", MinimalCollection);
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");
        folder.Write("tests/b.test.yaml", "name: B\nnot_a_key: 1\n");         // broken, but not picked

        var loaded = new YamlCollectionLoader().Load(folder.Path, file => file == "a.test.yaml");

        Assert.Equal(["A"], loaded.TestCases.Select(t => t.Name));
    }

    [Fact]
    public void ParseTest_reads_an_unsaved_test_like_a_file()
    {
        var test = new YamlCollectionLoader().ParseTest("name: Draft\nsteps:\n- { type: request, url: x }\n", "unsaved test");

        Assert.Equal("Draft", test.Name);
        Assert.Equal("unsaved test", test.SourceFile);
        Assert.False(string.IsNullOrEmpty(test.Steps[0].Id));                      // normalized like a loaded file
        Assert.Throws<InvalidOperationException>(() => new YamlCollectionLoader().ParseTest("name: D\nsetps: []\n", "unsaved test"));
    }
}
