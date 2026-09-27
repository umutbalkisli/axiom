using Axiom.Documents;
using Axiom.Parsing;
using Axiom.Services;

namespace Axiom.Tests.Services;

public class CollectionManagementServiceTests
{
    private static readonly CollectionManagementService Service = new();

    private static SaveTestCaseRequest Test(string name, string? fileName = null, params AssertionDocument[] assertions) => new()
    {
        Name = name,
        FileName = fileName,
        Endpoint = "/e",
        Method = "GET",
        Steps = [new StepDocument { Id = "r", Type = "request", Method = "GET", Url = "http://x", Assert = assertions.ToList() }],
    };

    private static SaveSharedStepsRequest Shared(string name, string? fileName = null, params StepDocument[] steps) =>
        new() { Name = name, FileName = fileName, Steps = steps.Length > 0 ? steps.ToList() : [new StepDocument { Id = "s", Type = "request", Method = "GET", Url = "x" }] };

    // ---- folders ----

    [Fact]
    public void Tests_in_folders_are_listed_with_their_relative_path_folder_and_id()
    {
        using var folder = new TempFolder();
        folder.Write("tests/top.test.yaml", "id: aaaaaaaaaaaaaaaa\nname: Top\nsteps: []\n");
        folder.Write("tests/orders/create.test.yaml", "name: Create\nsteps: []\n");
        folder.Write("tests/orders/refunds/create.test.yaml", "name: Refund\nsteps: []\n");

        var tests = Service.ListTests(folder.Path);

        Assert.Equal(["orders/create.test.yaml", "orders/refunds/create.test.yaml", "top.test.yaml"], tests.Select(t => t.FileName));
        Assert.Equal(["orders", "orders/refunds", ""], tests.Select(t => t.Folder));
        Assert.Equal("orders/refunds/create", tests[1].Id);
        Assert.Equal("aaaaaaaaaaaaaaaa", tests[2].TestId);
        Assert.Null(tests[0].TestId);
        Assert.Equal("Refund", Service.GetTest(folder.Path, "orders/refunds/create.test.yaml")!.Name);
    }

    [Fact]
    public void A_new_test_is_created_in_its_folder_with_an_id_that_saving_keeps()
    {
        using var folder = new TempFolder();

        var created = Service.SaveTest(folder.Path, new SaveTestCaseRequest { Name = "Create order", Folder = "Orders API/Happy path" });
        var id = Service.GetTest(folder.Path, created.FileName)!.Id;
        var renamed = Service.SaveTest(folder.Path, new SaveTestCaseRequest { Name = "Create big order", FileName = created.FileName, Folder = "elsewhere" });

        Assert.Equal("orders-api/happy-path/create-order.test.yaml", created.FileName);     // new folders follow the naming rules
        Assert.Matches("^[0-9a-f]{16}$", id);
        Assert.Equal("orders-api/happy-path/create-big-order.test.yaml", renamed.FileName); // stays in its folder; Folder is only for new tests
        Assert.Equal(id, Service.GetTest(folder.Path, renamed.FileName)!.Id);             // the id survives the rename
    }

    [Fact]
    public void A_test_written_before_ids_existed_gets_one_when_saved()
    {
        using var folder = new TempFolder();
        folder.Write("tests/old.test.yaml", "name: Old\nsteps: []\n");

        Service.SaveTest(folder.Path, new SaveTestCaseRequest { Name = "Old", FileName = "old.test.yaml" });

        Assert.Matches("^[0-9a-f]{16}$", Service.GetTest(folder.Path, "old")!.Id);
    }

    [Fact]
    public void The_same_file_name_can_exist_in_different_folders()
    {
        using var folder = new TempFolder();

        var a = Service.SaveTest(folder.Path, new SaveTestCaseRequest { Name = "Create", Folder = "orders" });
        var b = Service.SaveTest(folder.Path, new SaveTestCaseRequest { Name = "Create", Folder = "users" });

        Assert.Equal("orders/create.test.yaml", a.FileName);
        Assert.Equal("users/create.test.yaml", b.FileName);
    }

    [Fact]
    public void Moving_a_test_moves_the_file_unchanged_and_never_overwrites()
    {
        using var folder = new TempFolder();
        const string content = "# keep me\nid: 1111111111111111\nname: Create\nsteps: []   # odd spacing kept\n";
        folder.Write("tests/orders/create.test.yaml", content);
        folder.Write("tests/archive/create.test.yaml", "name: Someone else's\nsteps: []\n");

        var moved = Service.MoveTest(folder.Path, "orders/create.test.yaml", "archive");

        Assert.Equal("archive/create-2.test.yaml", moved);                                  // name taken there: next free one
        Assert.Equal(content, folder.Read("tests/archive/create-2.test.yaml"));             // moved, not re-saved
        Assert.Equal("name: Someone else's\nsteps: []\n", folder.Read("tests/archive/create.test.yaml"));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "tests", "orders")));      // the emptied folder is gone
        Assert.Equal("create-2.test.yaml", Service.MoveTest(folder.Path, "archive/create-2", ""));   // to the top level
        Assert.Equal("create-2.test.yaml", Service.MoveTest(folder.Path, "create-2", null));         // already there: nothing to do
        Assert.Throws<ArgumentException>(() => Service.MoveTest(folder.Path, "missing", "x"));
        Assert.Throws<ArgumentException>(() => Service.MoveTest(folder.Path, "create-2", "../outside"));
    }

    [Fact]
    public void A_folder_that_holds_other_files_is_not_removed_when_its_last_test_leaves()
    {
        using var folder = new TempFolder();
        folder.Write("tests/orders/create.test.yaml", "name: C\nsteps: []\n");
        folder.Write("tests/orders/README.md", "notes");

        Service.MoveTest(folder.Path, "orders/create", "");

        Assert.True(folder.Exists("tests/orders/README.md"));
    }

    [Fact]
    public void Renaming_a_folder_moves_its_tests_and_refuses_to_merge_into_an_existing_one()
    {
        using var folder = new TempFolder();
        folder.Write("tests/orders/create.test.yaml", "name: C\nsteps: []\n");
        folder.Write("tests/orders/refunds/full.test.yaml", "name: F\nsteps: []\n");
        folder.Write("tests/users/create.test.yaml", "name: U\nsteps: []\n");

        Assert.Throws<ArgumentException>(() => Service.RenameFolder(folder.Path, "orders", "users"));     // would merge
        Assert.Throws<ArgumentException>(() => Service.RenameFolder(folder.Path, "orders", "orders/inner")); // into itself
        Assert.Throws<ArgumentException>(() => Service.RenameFolder(folder.Path, "orders", "a/b/c"));      // refunds would be 4 deep
        Assert.Equal(["orders/create.test.yaml", "orders/refunds/full.test.yaml", "users/create.test.yaml"],
            Service.ListTests(folder.Path).Select(t => t.FileName));                                        // nothing changed

        var renamed = Service.RenameFolder(folder.Path, "orders", "Shop/Orders");

        Assert.Equal("shop/orders", renamed);
        Assert.Equal(["shop/orders/create.test.yaml", "shop/orders/refunds/full.test.yaml", "users/create.test.yaml"],
            Service.ListTests(folder.Path).Select(t => t.FileName));
    }

    [Fact]
    public void Deleting_a_folder_deletes_its_tests_but_keeps_other_files()
    {
        using var folder = new TempFolder();
        folder.Write("tests/orders/a.test.yaml", "name: A\nsteps: []\n");
        folder.Write("tests/orders/sub/b.test.yaml", "name: B\nsteps: []\n");
        folder.Write("tests/orders/notes.txt", "keep");
        folder.Write("tests/keep.test.yaml", "name: K\nsteps: []\n");

        var deleted = Service.DeleteFolder(folder.Path, "orders");

        Assert.Equal(2, deleted);
        Assert.Equal(["keep.test.yaml"], Service.ListTests(folder.Path).Select(t => t.FileName));
        Assert.True(folder.Exists("tests/orders/notes.txt"));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "tests", "orders", "sub")));
        Assert.Throws<ArgumentException>(() => Service.DeleteFolder(folder.Path, ""));
    }

    [Fact]
    public void A_shared_group_used_by_a_test_in_a_folder_cannot_be_deleted()
    {
        using var folder = new TempFolder();
        folder.Write("shared/login.shared.yaml", "name: Login\nsteps: []\n");
        folder.Write("tests/orders/a.test.yaml", "name: A\nsteps:\n- { id: i, type: include, ref: login }\n");

        var error = Assert.Throws<InvalidOperationException>(() => Service.DeleteShared(folder.Path, "login"));

        Assert.Contains("tests/orders/a.test.yaml", error.Message);
    }

    // ---- no overwrite, ever ----

    [Fact]
    public void Creating_and_moving_files_take_the_next_free_name_instead_of_overwriting()
    {
        using var folder = new TempFolder();
        var directory = Path.Combine(folder.Path, "d");
        folder.Write("d/a.test.yaml", "first");
        folder.Write("d/A-2.test.yaml", "other case");                       // taken in any letter case
        folder.Write("src.test.yaml", "moving");

        var created = SafeFiles.CreateUnique(directory, "a", ".test.yaml", "new");
        var moved = SafeFiles.MoveUnique(Path.Combine(folder.Path, "src.test.yaml"), directory, "a", ".test.yaml");

        Assert.Equal("a-3.test.yaml", created);
        Assert.Equal("a-4.test.yaml", moved);
        Assert.Equal("first", folder.Read("d/a.test.yaml"));
        Assert.Equal("other case", folder.Read("d/A-2.test.yaml"));
        Assert.Equal("moving", folder.Read("d/a-4.test.yaml"));
    }

    [Fact]
    public void A_change_of_letter_case_only_renames_the_same_file()
    {
        using var folder = new TempFolder();
        var directory = Path.Combine(folder.Path, "d");
        var source = folder.Write("d/Get-User.test.yaml", "content");

        var renamed = SafeFiles.MoveUnique(source, directory, "get-user", ".test.yaml");

        Assert.Equal("get-user.test.yaml", renamed);
        Assert.Equal(["get-user.test.yaml"], Directory.GetFiles(directory).Select(Path.GetFileName));
        Assert.Equal("content", folder.Read("d/get-user.test.yaml"));
    }

    // ---- cloning ----

    private const string Original = """
        # Checks the happy path; keep in sync with the API docs.
        id: 0123456789abcdef
        name: Get user
        description: reads one user
        endpoint: /users/{id}
        method: GET
        steps:
        - id: r
          type: request
          method: GET
          url: '{{base_url}}/users/1'   # the seeded user
          assert:
          - { source: status, operator: '==', expected: 200 }

        """;

    [Fact]
    public void Cloning_copies_the_file_as_it_is_and_changes_only_the_name()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\n");
        folder.Write("tests/get-user.test.yaml", Original);

        var (_, fileName) = Service.CloneTest(folder.Path, "get-user.test.yaml", "Get user (copy)");

        var cloneId = Service.GetTest(folder.Path, fileName)!.Id;
        Assert.Equal("get-user-copy.test.yaml", fileName);
        Assert.Matches("^[0-9a-f]{16}$", cloneId);
        Assert.NotEqual("0123456789abcdef", cloneId);                                          // a copy is a new test
        Assert.Equal(
            Original.Replace("name: Get user\n", "name: Get user (copy)\n").Replace("id: 0123456789abcdef\n", $"id: {cloneId}\n"),
            folder.Read("tests/get-user-copy.test.yaml"));
        Assert.Equal(Original, folder.Read("tests/get-user.test.yaml"));                     // the original is untouched
        Assert.Equal(["Get user", "Get user (copy)"], new YamlCollectionLoader().Load(folder.Path).TestCases.Select(t => t.Name).Order());
    }

    [Theory]
    [InlineData("Status: 200 # ok")]
    [InlineData("it's \"quoted\"")]
    [InlineData("- starts like a list")]
    [InlineData("Şifre değiştir (kopya)")]
    public void A_cloned_name_that_needs_quoting_reads_back_exactly(string name)
    {
        using var folder = new TempFolder();
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");

        var (_, fileName) = Service.CloneTest(folder.Path, "a", name);

        Assert.Equal(name, Service.GetTest(folder.Path, fileName)!.Name);
    }

    [Fact]
    public void A_multi_line_name_is_replaced_whole_and_a_missing_name_is_added()
    {
        using var folder = new TempFolder();
        folder.Write("tests/folded.test.yaml", "name: >\n  A long\n  name\nsteps: []\n");
        folder.Write("tests/nameless.test.yaml", "steps: []\n");

        var folded = Service.CloneTest(folder.Path, "folded", "Short");
        var nameless = Service.CloneTest(folder.Path, "nameless", "Named");

        Assert.Matches("^id: [0-9a-f]{16}\nname: Short\nsteps: \\[\\]\n$", folder.Read($"tests/{folded.FileName}"));
        Assert.Equal("Named", Service.GetTest(folder.Path, nameless.FileName)!.Name);
        Assert.Empty(Service.GetTest(folder.Path, nameless.FileName)!.Steps);
    }

    [Fact]
    public void Clones_never_overwrite_an_existing_file()
    {
        using var folder = new TempFolder();
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");

        var first = Service.CloneTest(folder.Path, "a", "A");
        var second = Service.CloneTest(folder.Path, "a", "A");

        Assert.Equal(["a-2.test.yaml", "a-3.test.yaml"], new[] { first.FileName, second.FileName });
    }

    [Fact]
    public void Cloning_a_missing_test_or_without_a_name_is_rejected()
    {
        using var folder = new TempFolder();
        folder.Write("tests/a.test.yaml", "name: A\nsteps: []\n");

        Assert.Throws<ArgumentException>(() => Service.CloneTest(folder.Path, "missing", "B"));
        Assert.Throws<ArgumentException>(() => Service.CloneTest(folder.Path, "a", "  "));
        Assert.Throws<ArgumentException>(() => Service.CloneTest(folder.Path, "../a", "B"));
    }

    // ---- test files ----

    [Fact]
    public void A_new_test_gets_a_unique_name_derived_from_its_name()
    {
        using var folder = new TempFolder();

        var first = Service.SaveTest(folder.Path, Test("Get one todo"));
        var second = Service.SaveTest(folder.Path, Test("Get one todo"));
        var third = Service.SaveTest(folder.Path, Test("Get one todo"));

        Assert.Equal("get-one-todo.test.yaml", first.FileName);
        Assert.Equal("get-one-todo-2.test.yaml", second.FileName);
        Assert.Equal("get-one-todo-3.test.yaml", third.FileName);
        Assert.Equal(3, folder.Files("tests").Length);       // nothing was overwritten
    }

    [Fact]
    public void Long_names_make_short_file_names_that_still_do_not_collide()
    {
        using var folder = new TempFolder();
        var a = Service.SaveTest(folder.Path, Test(new string('a', 80) + " one"));
        var b = Service.SaveTest(folder.Path, Test(new string('a', 80) + " two"));

        Assert.NotEqual(a.FileName, b.FileName);
        Assert.All(new[] { a, b }, r => Assert.True(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(r.FileName)).Length <= TestFileNames.MaxLength + 2));
    }

    [Fact]
    public void Saving_an_existing_file_updates_it_and_keeps_the_name_when_the_test_name_is_unchanged()
    {
        using var folder = new TempFolder();
        var created = Service.SaveTest(folder.Path, Test("Get one todo"));

        var updated = Service.SaveTest(folder.Path, Test("Get one todo", created.FileName));

        Assert.Equal(created.FileName, updated.FileName);
        Assert.Single(folder.Files("tests"));
    }

    [Fact]
    public void Renaming_a_test_renames_its_file_if_the_file_still_has_the_generated_name()
    {
        using var folder = new TempFolder();
        var created = Service.SaveTest(folder.Path, Test("Get one todo"));

        var renamed = Service.SaveTest(folder.Path, Test("Fetch a todo by id", created.FileName));

        Assert.Equal("fetch-a-todo-by-id.test.yaml", renamed.FileName);
        Assert.Equal(["fetch-a-todo-by-id.test.yaml"], folder.Files("tests"));   // the old file is gone
    }

    [Fact]
    public void A_file_the_user_renamed_is_left_alone()
    {
        using var folder = new TempFolder();
        Service.SaveTest(folder.Path, Test("Get one todo"));
        File.Move(Path.Combine(folder.Path, "tests", "get-one-todo.test.yaml"), Path.Combine(folder.Path, "tests", "my-custom-name.test.yaml"));

        var saved = Service.SaveTest(folder.Path, Test("Totally different", "my-custom-name"));

        Assert.Equal("my-custom-name.test.yaml", saved.FileName);
    }

    [Fact]
    public void Renaming_onto_a_name_that_is_taken_adds_a_suffix()
    {
        using var folder = new TempFolder();
        Service.SaveTest(folder.Path, Test("Alpha"));
        var beta = Service.SaveTest(folder.Path, Test("Beta"));

        var renamed = Service.SaveTest(folder.Path, Test("Alpha", beta.FileName));

        Assert.Equal("alpha-2.test.yaml", renamed.FileName);
        Assert.Equal(2, folder.Files("tests").Length);
    }

    [Fact]
    public void Saving_with_a_file_name_that_does_not_exist_creates_a_new_file()
    {
        using var folder = new TempFolder();
        var saved = Service.SaveTest(folder.Path, Test("Whatever", "gone"));
        Assert.Equal("gone.test.yaml", saved.FileName);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/../../evil")]
    [InlineData("/etc/passwd")]
    [InlineData("a/b/c/d/too-deep")]
    [InlineData("con")]
    public void Unsafe_file_names_are_rejected(string fileName)
    {
        using var folder = new TempFolder();
        Assert.Throws<ArgumentException>(() => Service.SaveTest(folder.Path, Test("x", fileName)));
        Assert.Throws<ArgumentException>(() => Service.GetTest(folder.Path, fileName));
        Assert.Throws<ArgumentException>(() => Service.DeleteTest(folder.Path, fileName));
    }

    [Fact]
    public void A_saved_test_round_trips_including_assertion_options_and_include_steps()
    {
        using var folder = new TempFolder();
        var request = Test("Round trip", null,
            new AssertionDocument { Source = "body", Path = "@http.status", Operator = "==", Expected = 200, Aggregate = "SUM", Strict = true, CaseSensitive = true, Tolerance = 0.5m });
        request.Steps.Add(new StepDocument { Id = "inc", Type = "include", Ref = " login " });
        request.Variables = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "text" };

        var saved = Service.SaveTest(folder.Path, request);
        var loaded = Service.GetTest(folder.Path, saved.FileName)!;

        var assertion = loaded.Steps[0].Assert[0];
        Assert.Equal("@http.status", assertion.Path);
        Assert.Equal("sum", assertion.Aggregate);          // stored lower-case
        Assert.True(assertion.Strict);
        Assert.True(assertion.CaseSensitive);
        Assert.Equal(0.5m, assertion.Tolerance);
        Assert.Equal("login", loaded.Steps[1].Ref);         // trimmed
        Assert.Equal(2, loaded.Variables.Count);
        Assert.Contains("'@http.status'", folder.Read("tests/" + saved.FileName));   // YAML needs the quotes
    }

    [Fact]
    public void Options_that_are_off_are_not_written()
    {
        using var folder = new TempFolder();
        var saved = Service.SaveTest(folder.Path, Test("Plain", null, new AssertionDocument { Source = "status", Operator = "==", Expected = 200, Strict = false }));
        var yaml = folder.Read("tests/" + saved.FileName);
        Assert.DoesNotContain("strict", yaml);
        Assert.DoesNotContain("case_sensitive", yaml);
        Assert.DoesNotContain("tolerance", yaml);
        Assert.DoesNotContain("aggregate", yaml);
    }

    [Fact]
    public void Listing_shows_display_names_endpoints_and_falls_back_to_the_file_name()
    {
        using var folder = new TempFolder();
        Service.SaveTest(folder.Path, Test("Get one todo"));
        folder.Write("tests/no-name.test.yaml", "steps:\n- { id: r, type: request, method: POST, url: x }\n");

        var list = Service.ListTests(folder.Path);

        Assert.Equal(["get-one-todo.test.yaml", "no-name.test.yaml"], list.Select(t => t.FileName));
        Assert.Equal("Get one todo", list[0].Name);
        Assert.Equal("/e", list[0].Endpoint);
        Assert.Equal("No Name", list[1].Name);            // derived from the file name
        Assert.Equal("POST", list[1].Method);             // derived from the first request step
        Assert.Empty(Service.ListTests(Path.Combine(folder.Path, "nope")));
    }

    [Fact]
    public void Deleting_removes_the_file_and_is_quiet_when_it_is_gone()
    {
        using var folder = new TempFolder();
        var saved = Service.SaveTest(folder.Path, Test("Temp"));

        Service.DeleteTest(folder.Path, saved.FileName);
        Service.DeleteTest(folder.Path, saved.FileName);

        Assert.Empty(folder.Files("tests"));
        Assert.Null(Service.GetTest(folder.Path, saved.FileName));
        Assert.False(Service.TestExists(folder.Path, saved.FileName));
    }

    // ---- collection settings ----

    [Fact]
    public void Collection_settings_save_and_keep_the_parts_that_were_not_sent()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: C\ndescription: keep me\nrun_settings: { max_parallel_test_cases: 7, step_timeout_seconds: 9 }\nsecrets:\n  s: { provider: env, key: K }\n");

        Service.SaveCollectionSettings(folder.Path, new() { ["base_url"] = "http://x" }, new() { ["db"] = new Dictionary<string, object?> { ["provider"] = "sqlite" } });
        var afterSettings = Service.GetCollection(folder.Path)!;

        Assert.Equal("keep me", afterSettings.Description);
        Assert.Equal(7, afterSettings.RunSettings.MaxParallelTestCases);
        Assert.Equal("env", afterSettings.Secrets["s"].Provider);            // secrets survive when not sent
        Assert.Equal("http://x", afterSettings.Variables["base_url"]);

        Service.SaveCollectionSecrets(folder.Path, new() { ["t"] = new SecretReference { Provider = "vault", Key = "a#b" } });
        Assert.Equal(["t"], Service.GetCollection(folder.Path)!.Secrets.Keys);
    }

    [Fact]
    public void Missing_collection_files_are_reported()
    {
        using var folder = new TempFolder();
        Assert.Null(Service.GetCollection(folder.Path));
        Assert.Throws<FileNotFoundException>(() => Service.SaveCollectionVariables(folder.Path, []));
    }

    // ---- shared steps ----

    [Fact]
    public void Shared_steps_keep_their_file_name_when_renamed_because_tests_refer_to_them()
    {
        using var folder = new TempFolder();
        var created = Service.SaveShared(folder.Path, Shared("Get auth token"));

        var renamed = Service.SaveShared(folder.Path, Shared("Totally renamed", created.FileName));

        Assert.Equal("get-auth-token.shared.yaml", created.FileName);
        Assert.Equal(created.FileName, renamed.FileName);
        Assert.Equal("Totally renamed", Service.GetShared(folder.Path, created.FileName)!.Name);
    }

    [Fact]
    public void Shared_steps_store_a_valid_run_mode_and_default_to_each()
    {
        using var folder = new TempFolder();
        var once = Service.SaveShared(folder.Path, new SaveSharedStepsRequest { Name = "a", Run = " ONCE ", Steps = Shared("x").Steps });
        var other = Service.SaveShared(folder.Path, new SaveSharedStepsRequest { Name = "b", Run = "nonsense", Steps = Shared("x").Steps });

        Assert.Equal("once", Service.GetShared(folder.Path, once.FileName)!.Run);
        Assert.Equal("each", Service.GetShared(folder.Path, other.FileName)!.Run);
    }

    [Fact]
    public void Listing_shared_steps_reports_the_variables_each_provides_including_through_includes()
    {
        using var folder = new TempFolder();
        Service.SaveShared(folder.Path, Shared("Get auth token", null, new StepDocument { Id = "l", Type = "request", Method = "POST", Url = "x", SaveAs = "token_resp" }));
        Service.SaveShared(folder.Path, Shared("Login and pick", null,
            new StepDocument { Id = "i", Type = "include", Ref = "get-auth-token" },
            new StepDocument { Id = "p", Type = "request", Method = "GET", Url = "x", SaveAs = "item" }));

        var list = Service.ListShared(folder.Path);

        Assert.Equal(["token_resp"], list.Single(s => s.Id == "get-auth-token").Provides);
        Assert.Equal(["token_resp", "item"], list.Single(s => s.Id == "login-and-pick").Provides);
        Assert.Equal(2, list.Single(s => s.Id == "login-and-pick").StepCount);
    }

    [Fact]
    public void A_shared_group_that_is_still_included_cannot_be_deleted()
    {
        using var folder = new TempFolder();
        var group = Service.SaveShared(folder.Path, Shared("Get auth token"));
        var nested = Service.SaveShared(folder.Path, Shared("Wrapper", null, new StepDocument { Id = "i", Type = "include", Ref = "get-auth-token" }));
        var test = new SaveTestCaseRequest { Name = "T", Endpoint = "/e", Steps = [new StepDocument { Id = "a", Type = "include", Ref = "get-auth-token" }] };
        var testFile = Service.SaveTest(folder.Path, test);

        var ex = Assert.Throws<InvalidOperationException>(() => Service.DeleteShared(folder.Path, group.FileName));
        Assert.Contains("tests/" + testFile.FileName, ex.Message);
        Assert.Contains("shared/" + nested.FileName, ex.Message);

        Service.DeleteTest(folder.Path, testFile.FileName);
        Service.DeleteShared(folder.Path, nested.FileName);   // nothing includes the wrapper
        Service.DeleteShared(folder.Path, group.FileName);    // now nothing includes the group either
        Assert.Empty(Service.ListShared(folder.Path));
    }
}

public class CollectionInitializerTests
{
    [Fact]
    public async Task Creates_the_folders_and_a_starter_collection_that_loads()
    {
        using var folder = new TempFolder();
        var target = Path.Combine(folder.Path, "new");

        await new CollectionInitializer().InitializeAsync(target, "My API");

        Assert.True(Directory.Exists(Path.Combine(target, "tests")));
        var loaded = new YamlCollectionLoader().Load(target);
        Assert.Equal("My API", loaded.Collection.Name);
        Assert.Equal("https://api.example.com", loaded.Collection.Variables["base_url"]);
        Assert.Equal("application/json", loaded.Collection.RequestDefaults.Headers["Accept"]);
    }

    [Theory]
    [InlineData("Weird: name # here")]
    [InlineData("quote ' and \" chars")]
    [InlineData("- starts with dash")]
    public async Task Names_with_yaml_special_characters_survive(string name)
    {
        using var folder = new TempFolder();
        await new CollectionInitializer().InitializeAsync(folder.Path, name);
        Assert.Equal(name, new YamlCollectionLoader().Load(folder.Path).Collection.Name);
    }

    [Fact]
    public async Task An_existing_collection_file_is_never_overwritten()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", "name: Original\n");

        await new CollectionInitializer().InitializeAsync(folder.Path, "Other");

        Assert.Equal("Original", new YamlCollectionLoader().Load(folder.Path).Collection.Name);
    }
}

public class OpenApiImporterTests
{
    private const string Spec = """
        {"openapi":"3.0.0","paths":{
          "/pets":{"get":{"operationId":"listPets","summary":"List pets"},"post":{"operationId":"createPet","summary":"Create a pet"}},
          "/pets/{id}":{"get":{"summary":"Get a pet"},"parameters":[]}}}
        """;

    private static OpenApiImporter Importer(StubHandler handler)
    {
        var manager = new CollectionManagementService();
        return new OpenApiImporter(new HttpClient(handler), new CollectionInitializer(), manager);
    }

    [Fact]
    public async Task Creates_one_test_per_operation_with_a_status_assertion()
    {
        using var folder = new TempFolder();
        var handler = new StubHandler(_ => Http.Json(Spec));

        var imported = await Importer(handler).ImportFromUrlAsync(folder.Path, "Pets", "https://example.com/openapi.json");

        Assert.Equal(3, imported);
        Assert.Equal(["createpet.test.yaml", "get-pets-id.test.yaml", "listpets.test.yaml"], folder.Files("tests"));
        var test = new CollectionManagementService().GetTest(folder.Path, "listpets")!;
        Assert.Equal("List pets", test.Name);
        Assert.Equal("/pets", test.Endpoint);
        Assert.Equal("{{base_url}}/pets", test.Steps[0].Url);
        Assert.Equal("status", test.Steps[0].Assert[0].Source);
        Assert.True(folder.Exists("collection.yaml"));
    }

    [Fact]
    public async Task Importing_again_does_not_overwrite_tests_that_already_exist()
    {
        using var folder = new TempFolder();
        var importer = Importer(new StubHandler(_ => Http.Json(Spec)));
        await importer.ImportFromUrlAsync(folder.Path, "Pets", "https://example.com/openapi.json");
        var edited = folder.Read("tests/listpets.test.yaml") + "# edited by hand\n";
        folder.Write("tests/listpets.test.yaml", edited);

        var second = await importer.ImportFromUrlAsync(folder.Path, "Pets", "https://example.com/openapi.json");

        Assert.Equal(0, second);
        Assert.Equal(edited, folder.Read("tests/listpets.test.yaml"));
    }

    [Fact]
    public async Task Yaml_documents_are_supported_and_only_http_urls_are_accepted()
    {
        using var folder = new TempFolder();
        var yaml = "openapi: 3.0.0\npaths:\n  /a:\n    get:\n      operationId: getA\n";
        var importer = Importer(new StubHandler(_ => Http.Text(yaml)));

        Assert.Equal(1, await importer.ImportFromUrlAsync(folder.Path, "A", "https://example.com/spec.yaml"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportFromUrlAsync(folder.Path, "A", "file:///etc/passwd"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportFromUrlAsync(folder.Path, "A", "not a url"));
    }
}
