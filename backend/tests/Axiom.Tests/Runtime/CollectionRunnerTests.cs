using System.Net;
using System.Text.Json;
using Axiom.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Tests.Runtime;

[Collection("Environment")]
public class CollectionRunnerTests
{
    private const string CollectionHeader = "name: Demo\nvariables: { base_url: 'http://api.test' }\nrun_settings: { max_parallel_test_cases: 4, step_timeout_seconds: 10 }\n";

    private static string Test(string name, string url = "{{base_url}}/x", int expectedStatus = 200) =>
        $"name: {name}\nsteps:\n- {{ id: r, type: request, method: GET, url: '{url}', assert: [ {{ source: status, operator: '==', expected: {expectedStatus} }} ] }}\n";

    private static async Task<CollectionExecutionResult> Run(TempFolder folder, StubHandler handler, RunOptions? options = null)
    {
        await using var services = Build.Services(handler);
        return await services.GetRequiredService<CollectionRunner>().RunAsync(folder.Path, options ?? new RunOptions());
    }

    [Fact]
    public async Task Runs_every_test_and_reports_passed_failed_and_error_apart()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a-pass.test.yaml", Test("passes"));
        folder.Write("tests/b-fail.test.yaml", Test("fails", expectedStatus: 404));
        folder.Write("tests/c-error.test.yaml", Test("errors", url: "{{base_url}}/{{undefined_variable}}"));

        var result = await Run(folder, new StubHandler(_ => Http.Json("{}")));

        Assert.Equal(["passes", "fails", "errors"], result.TestCases.Select(t => t.Name));      // ordered by file
        Assert.Equal([RunOutcome.Passed, RunOutcome.Failed, RunOutcome.Error], result.TestCases.Select(t => t.Outcome));
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(2, result.UnsuccessfulCount);
        Assert.Equal(100.0 / 3, result.SuccessRate, 5);
    }

    [Fact]
    public async Task An_empty_collection_runs_with_no_tests()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);

        var result = await Run(folder, new StubHandler(_ => Http.Json("{}")));

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.SuccessRate);
        Assert.Equal(0, result.UnsuccessfulCount);
    }

    [Fact]
    public async Task A_run_once_login_is_executed_a_single_time_for_all_parallel_tests()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("shared/auth.shared.yaml", "name: Auth\nrun: once\nsteps:\n- { id: login, type: request, method: POST, url: '{{base_url}}/login', save_as: session }\n");
        for (var i = 0; i < 6; i++)
        {
            folder.Write($"tests/t{i}.test.yaml", $$$"""
                name: t{{{i}}}
                steps:
                - { id: a, type: include, ref: auth }
                - id: call
                  type: request
                  method: GET
                  url: '{{base_url}}/secure'
                  headers: { Authorization: 'Bearer {{session.token}}' }
                  assert: [ { source: status, operator: '==', expected: 200 } ]
                """);
        }

        var handler = new StubHandler(request => request.Method == HttpMethod.Post
            ? Http.Json("{\"token\":\"tok-1\"}")
            : request.Headers.GetValueOrDefault("Authorization") == "Bearer tok-1" ? Http.Json("{}") : Http.Json("{}", HttpStatusCode.Unauthorized));

        var result = await Run(folder, handler);

        Assert.Equal(6, result.PassedCount);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));       // one login for six tests
        Assert.Equal(6, handler.Requests.Count(r => r.Method == HttpMethod.Get));
    }

    [Fact]
    public async Task Secrets_are_resolved_before_anything_runs_and_never_appear_in_the_results()
    {
        using var env = new EnvironmentScope().Set("AXIOM_RUNNER_TOKEN", "super-secret-token-value");
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + "secrets:\n  token: { provider: env, key: AXIOM_RUNNER_TOKEN }\n");
        folder.Write("tests/a.test.yaml", """
            name: uses a secret
            steps:
            - id: r
              type: request
              method: GET
              url: '{{base_url}}/x'
              headers: { Authorization: 'Bearer {{secret.token}}' }
              assert:
              - { source: status, operator: '==', expected: 999 }
              - { source: body, path: echo, operator: '==', expected: '{{secret.token}}' }
            """);
        var handler = new StubHandler(_ => Http.Json("{\"echo\":\"something else\"}"));

        var result = await Run(folder, handler);

        Assert.Equal("Bearer super-secret-token-value", handler.Requests[0].Headers["Authorization"]);   // the real value was sent
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        Assert.DoesNotContain("super-secret-token-value", json);
        Assert.Contains("********", json);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task A_secret_that_cannot_be_read_fails_the_run_before_any_request_is_sent()
    {
        using var env = new EnvironmentScope().Set("AXIOM_RUNNER_MISSING", null);
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + "secrets:\n  token: { provider: env, key: AXIOM_RUNNER_MISSING }\n");
        folder.Write("tests/a.test.yaml", Test("a"));
        var handler = new StubHandler(_ => Http.Json("{}"));

        var ex = await Assert.ThrowsAsync<SecretResolutionException>(() => Run(folder, handler));

        Assert.Contains("token", ex.Message);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task The_selected_environment_chooses_each_secrets_source()
    {
        using var env = new EnvironmentScope().Set("AXIOM_RUNNER_DEFAULT", "default-value").Set("AXIOM_RUNNER_CI", "ci-value");
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + """
            secrets:
              key:
                provider: env
                key: AXIOM_RUNNER_DEFAULT
                environments:
                  ci: { provider: env, key: AXIOM_RUNNER_CI }
            """);
        folder.Write("tests/a.test.yaml", "name: a\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}/x', headers: { X-Key: '{{secret.key}}' } }\n");

        var defaultHandler = new StubHandler(_ => Http.Json("{}"));
        await Run(folder, defaultHandler);
        var ciHandler = new StubHandler(_ => Http.Json("{}"));
        await Run(folder, ciHandler, new RunOptions { Environment = "CI" });

        Assert.Equal("default-value", defaultHandler.Requests[0].Headers["X-Key"]);
        Assert.Equal("ci-value", ciHandler.Requests[0].Headers["X-Key"]);
    }

    [Fact]
    public async Task Values_handed_in_for_local_secrets_are_used_for_that_run_only()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + "secrets:\n  pw: { provider: local, key: pw }\n");
        folder.Write("tests/a.test.yaml", "name: a\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}/x', headers: { X-Pw: '{{secret.pw}}' } }\n");

        var handler = new StubHandler(_ => Http.Json("{}"));
        await Run(folder, handler, new RunOptions { LocalSecrets = new Dictionary<string, string> { ["pw"] = "typed-in-the-app" } });

        Assert.Equal("typed-in-the-app", handler.Requests[0].Headers["X-Pw"]);
        await Assert.ThrowsAsync<SecretResolutionException>(() => Run(folder, handler));      // nothing was remembered
    }

    [Fact]
    public async Task Variables_that_embed_secrets_are_expanded_once_for_every_use()
    {
        using var env = new EnvironmentScope().Set("AXIOM_RUNNER_BEARER", "abc");
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader.Replace("variables: {", "variables: { auth: 'Bearer {{secret.bearer}}',") + "secrets:\n  bearer: { provider: env, key: AXIOM_RUNNER_BEARER }\n");
        folder.Write("tests/a.test.yaml", "name: a\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}/x', headers: { Authorization: '{{auth}}' } }\n");

        var handler = new StubHandler(_ => Http.Json("{}"));
        await Run(folder, handler);

        Assert.Equal("Bearer abc", handler.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task Test_variables_override_collection_variables()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a.test.yaml", "name: a\nvariables: { base_url: 'http://override.test' }\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}/x' }\n");

        var handler = new StubHandler(_ => Http.Json("{}"));
        await Run(folder, handler);

        Assert.Equal("http://override.test/x", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Tests_run_in_parallel_up_to_the_configured_limit()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader.Replace("max_parallel_test_cases: 4", "max_parallel_test_cases: 3"));
        for (var i = 0; i < 9; i++)
        {
            folder.Write($"tests/t{i}.test.yaml", Test($"t{i}"));
        }

        int concurrent = 0, peak = 0;
        var handler = new StubHandler(_ =>
        {
            var now = Interlocked.Increment(ref concurrent);
            int seen;
            while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
            }

            Thread.Sleep(80);
            Interlocked.Decrement(ref concurrent);
            return Http.Json("{}");
        });

        var result = await Run(folder, handler);

        Assert.Equal(9, result.PassedCount);
        Assert.InRange(peak, 2, 3);
    }

    [Fact]
    public async Task A_folder_that_is_not_a_collection_is_an_error()
    {
        using var folder = new TempFolder();
        await Assert.ThrowsAsync<FileNotFoundException>(() => Run(folder, new StubHandler(_ => Http.Json("{}"))));
    }

    [Fact]
    public async Task Only_the_picked_tests_run_by_file_name_or_id()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a.test.yaml", Test("a"));
        folder.Write("tests/b.test.yaml", Test("b"));
        folder.Write("tests/c.test.yaml", Test("c"));

        var result = await Run(folder, new StubHandler(_ => Http.Json("{}")), new RunOptions { Tests = ["a.test.yaml", "C"] });

        Assert.Equal(["a", "c"], result.TestCases.Select(t => t.Name));
    }

    [Fact]
    public async Task A_whole_folder_can_be_picked_and_results_carry_the_relative_path_and_id()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/orders/create.test.yaml", "id: 00000000000000aa\n" + Test("orders create"));
        folder.Write("tests/orders/refunds/create.test.yaml", Test("refund create"));
        folder.Write("tests/users/create.test.yaml", Test("users create"));

        var result = await Run(folder, new StubHandler(_ => Http.Json("{}")), new RunOptions { Tests = ["orders/"] });

        Assert.Equal(["orders/create.test.yaml", "orders/refunds/create.test.yaml"], result.TestCases.Select(t => t.FileName));
        Assert.Equal(["00000000000000aa", null], result.TestCases.Select(t => t.TestId));
        var one = await Run(folder, new StubHandler(_ => Http.Json("{}")), new RunOptions { Tests = ["users/create"] });
        Assert.Equal(["users create"], one.TestCases.Select(t => t.Name));        // same file name in another folder is not picked
    }

    [Fact]
    public async Task Two_tests_with_the_same_id_stop_the_run_with_both_files_named()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a.test.yaml", "id: 00000000000000aa\n" + Test("a"));
        folder.Write("tests/copies/a.test.yaml", "id: 00000000000000aa\n" + Test("a copied by hand"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(folder, new StubHandler(_ => Http.Json("{}"))));

        Assert.Contains("a.test.yaml and copies/a.test.yaml", error.Message);
        Assert.Contains("'00000000000000aa'", error.Message);
    }

    [Fact]
    public async Task Progress_is_reported_before_the_first_test_and_after_each_one()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a.test.yaml", Test("a"));
        folder.Write("tests/b.test.yaml", Test("b", expectedStatus: 404));
        var started = -1;
        var finished = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Run(folder, new StubHandler(_ => Http.Json("{}")), new RunOptions
        {
            OnStarted = total =>
            {
                Assert.Empty(finished);
                started = total;
            },
            OnTestCompleted = test => finished.Add(test.Name),
        });

        Assert.Equal(2, started);
        Assert.Equal(["a", "b"], finished.Order());
    }

    [Fact]
    public async Task Cancelling_stops_the_run_instead_of_reporting_the_running_step_as_broken()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        folder.Write("tests/a.test.yaml", Test("a"));
        using var cancel = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        var handler = new DelayingHandler(async token =>
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        await using var services = Build.Services(handler);

        var reported = new List<TestCaseExecutionResult>();

        var run = services.GetRequiredService<CollectionRunner>().RunAsync(folder.Path, new RunOptions { OnTestCompleted = reported.Add }, cancel.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(reported);                    // no "A task was canceled" error result for the interrupted test
    }

    [Fact]
    public async Task A_preview_runs_up_to_the_picked_step_and_returns_what_it_received()
    {
        using var env = new EnvironmentScope().Set("AXIOM_PREVIEW_TOKEN", "preview-secret-value");
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + "secrets:\n  token: { provider: env, key: AXIOM_PREVIEW_TOKEN }\n");
        folder.Write("tests/broken.test.yaml", "not_a_key: 1\n");               // other tests are not loaded
        var test = new Axiom.Parsing.YamlCollectionLoader().ParseTest("""
            name: draft
            steps:
            - { id: first, type: request, method: GET, url: '{{base_url}}/one' }
            - { id: second, type: request, method: GET, url: '{{base_url}}/two', assert: [ { source: status, operator: '==', expected: 201 } ] }
            - { id: third, type: request, method: GET, url: '{{base_url}}/three' }
            """, "unsaved test");
        var handler = new StubHandler(request => request.Uri.AbsolutePath == "/two"
            ? Http.Json("{\"echo\":\"preview-secret-value\",\"id\":7}", headers: ("X-Trace", "abc"))
            : Http.Json("{}"));
        await using var services = Build.Services(handler);

        var preview = await services.GetRequiredService<CollectionRunner>().PreviewAsync(folder.Path, test, 1, new RunOptions());

        Assert.Equal(["/one", "/two"], handler.Requests.Select(r => r.Uri.AbsolutePath));     // the third step did not run
        Assert.Equal(["first", "second"], preview.Result.Steps.Select(s => s.Id));
        Assert.Equal(RunOutcome.Failed, preview.Result.Steps[1].Outcome);                     // its assertions still count
        var response = Assert.IsType<StepResponse>(preview.Response);
        Assert.Equal("second", response.StepId);
        Assert.Equal(200, response.Status);
        Assert.Equal("abc", response.Headers!["x-trace"]);
        Assert.Equal("{\"echo\":\"********\",\"id\":7}", response.Body);                     // secrets are masked
        Assert.False(response.BodyTruncated);
    }

    [Fact]
    public async Task A_preview_shows_the_request_as_it_was_sent_with_templates_resolved_and_secrets_masked()
    {
        using var env = new EnvironmentScope().Set("AXIOM_PREVIEW_TOKEN", "preview-secret-value");
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader
            + "secrets:\n  token: { provider: env, key: AXIOM_PREVIEW_TOKEN }\n"
            + "request_defaults: { headers: { X-Client: axiom } }\n");
        var test = new Axiom.Parsing.YamlCollectionLoader().ParseTest("""
            name: draft
            variables: { user_id: 42 }
            steps:
            - id: create
              type: request
              method: POST
              url: '{{base_url}}/users/{{user_id}}'
              query_params: { verbose: 'true' }
              headers: { Authorization: 'Bearer {{secret.token}}' }
              body: '{ "id": {{user_id}} }'
            """, "unsaved test");
        await using var services = Build.Services(new StubHandler(_ => Http.Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError)));

        var preview = await services.GetRequiredService<CollectionRunner>().PreviewAsync(folder.Path, test, 0, new RunOptions());

        var sent = preview.Response!.Request!;
        Assert.Equal(500, preview.Response.Status);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("http://api.test/users/42?verbose=true", sent.Url);
        Assert.Equal("{ \"id\": 42 }", sent.Body);
        Assert.Equal("Bearer ********", sent.Headers["authorization"]);
        Assert.Equal("axiom", sent.Headers["x-client"]);                              // collection defaults are included
        Assert.StartsWith("application/json", sent.Headers["content-type"]);
        Assert.Equal("application/json", sent.Headers["accept"]);
    }

    [Fact]
    public async Task A_preview_shows_what_was_sent_even_when_nothing_came_back()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        var test = new Axiom.Parsing.YamlCollectionLoader().ParseTest(
            "name: draft\nsteps:\n- { id: r, type: request, method: GET, url: '{{base_url}}/down' }\n", "unsaved test");
        await using var services = Build.Services(new StubHandler(_ => throw new HttpRequestException("Connection refused")));

        var preview = await services.GetRequiredService<CollectionRunner>().PreviewAsync(folder.Path, test, 0, new RunOptions());

        Assert.Equal("Connection refused", preview.Result.Steps[0].Error);
        Assert.Null(preview.Response!.Status);
        Assert.Equal("http://api.test/down", preview.Response.Request!.Url);
    }

    [Fact]
    public async Task A_preview_of_a_sql_step_returns_its_rows()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader + "connections:\n  db: { provider: sqlite, connection_string: 'Data Source=:memory:' }\n");
        var test = new Axiom.Parsing.YamlCollectionLoader().ParseTest(
            "name: draft\nsteps:\n- { id: q, type: db_query, connection: db, sql: 'SELECT 1 AS a UNION ALL SELECT 2' }\n", "unsaved test");
        await using var services = Build.Services(new StubHandler(_ => Http.Json("{}")));

        var preview = await services.GetRequiredService<CollectionRunner>().PreviewAsync(folder.Path, test, 0, new RunOptions());

        Assert.Equal("SELECT 1 AS a UNION ALL SELECT 2", preview.Response!.Sql);
        Assert.Equal(2, preview.Response.RowCount);
        Assert.Equal([1L, 2L], preview.Response.Rows!.Select(row => row["a"]));
        Assert.Null(preview.Response.Status);
    }

    [Fact]
    public async Task A_preview_of_a_step_that_does_not_exist_is_rejected()
    {
        using var folder = new TempFolder();
        folder.Write("collection.yaml", CollectionHeader);
        var test = new TestCaseDefinition { Name = "empty" };
        await using var services = Build.Services(new StubHandler(_ => Http.Json("{}")));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            services.GetRequiredService<CollectionRunner>().PreviewAsync(folder.Path, test, 0, new RunOptions()));
    }

    private sealed class DelayingHandler(Func<CancellationToken, Task> wait) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await wait(cancellationToken);
            return Http.Json("{}");
        }
    }
}
