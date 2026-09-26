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
        folder.Write("collection.yaml", CollectionHeader + "secrets:\n  token: { provider: env, key: AXIOM_RUNNER_TOKEN }\nvariables_note: ignored\n");
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
}
