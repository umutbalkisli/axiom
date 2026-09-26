using System.Net;
using Axiom.Secrets;

namespace Axiom.Tests.Runtime;

public class RequestStepExecutorTests
{
    private static async Task<(StepExecutionResult Result, StepExecutionContext Context, StubHandler Handler)> Run(
        StepDefinition step,
        Func<RecordedRequest, HttpResponseMessage>? respond = null,
        CollectionDefinition? collection = null,
        Dictionary<string, object?>? variables = null,
        RunSecrets? secrets = null)
    {
        var handler = new StubHandler(respond ?? (_ => Http.Json("{\"ok\":true}")));
        var executor = new RequestStepExecutor(new HttpClient(handler), Build.Engine());
        var runner = new StepRunner([executor]);
        var context = Build.Context(collection ?? Build.Collection(), runner, variables: variables ?? new(StringComparer.OrdinalIgnoreCase), secrets: secrets);
        var results = await runner.RunAsync(context, [step], default);
        return (results[0], context, handler);
    }

    private static Dictionary<string, object?> Vars(params (string, object?)[] values) =>
        new(values.ToDictionary(v => v.Item1, v => v.Item2), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Sends_the_method_url_query_parameters_headers_and_body_with_variables_resolved()
    {
        var step = Build.Request("r", "{{base}}/todos", "post");
        step.QueryParams = new(StringComparer.OrdinalIgnoreCase) { ["page"] = "{{page}}", ["q"] = "a b&c" };
        step.Headers = new(StringComparer.OrdinalIgnoreCase) { ["X-Trace"] = "id-{{page}}" };
        step.Body = "{\"title\":\"{{title}}\"}";

        var (result, _, handler) = await Run(step, variables: Vars(("base", "http://api.test"), ("page", 2), ("title", "Hello")));

        var request = Assert.Single(handler.Requests);
        Assert.True(result.Passed);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test/todos?page=2&q=a%20b%26c", request.Uri.AbsoluteUri);
        Assert.Equal("id-2", request.Headers["X-Trace"]);
        Assert.Equal("{\"title\":\"Hello\"}", request.Body);
        Assert.StartsWith("application/json", request.ContentType);
    }

    [Fact]
    public async Task Existing_query_strings_are_extended_and_collection_default_headers_apply_unless_overridden()
    {
        var collection = Build.Collection();
        collection.RequestDefaults.Headers = new(StringComparer.OrdinalIgnoreCase) { ["X-Default"] = "d", ["X-Both"] = "default" };
        var step = Build.Request("r", "http://x/a?b=1");
        step.QueryParams = new(StringComparer.OrdinalIgnoreCase) { ["c"] = "2" };
        step.Headers = new(StringComparer.OrdinalIgnoreCase) { ["X-Both"] = "step" };

        var (_, _, handler) = await Run(step, collection: collection);

        var request = handler.Requests[0];
        Assert.Equal("http://x/a?b=1&c=2", request.Uri.ToString());
        Assert.Equal("d", request.Headers["X-Default"]);
        Assert.Contains("step", request.Headers["X-Both"]);
    }

    [Fact]
    public async Task Accepts_json_by_default_and_respects_an_accept_header_that_was_set()
    {
        var (_, _, plain) = await Run(Build.Request("r", "http://x"));
        Assert.Equal("application/json", plain.Requests[0].Headers["Accept"]);

        var custom = Build.Request("r", "http://x");
        custom.Headers = new(StringComparer.OrdinalIgnoreCase) { ["Accept"] = "text/csv" };
        var (_, _, handler) = await Run(custom);
        Assert.Equal("text/csv", handler.Requests[0].Headers["Accept"]);
    }

    [Fact]
    public async Task A_content_type_header_set_on_a_request_with_a_body_is_kept()
    {
        var step = Build.Request("r", "http://x", "POST");
        step.Body = "<a/>";
        step.Headers = new(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/xml" };

        var (_, _, handler) = await Run(step);

        Assert.StartsWith("application/xml", handler.Requests[0].ContentType);
    }

    [Fact]
    public async Task Evaluates_assertions_on_status_body_headers_body_text_and_duration()
    {
        var step = Build.Request("r", "http://x", "GET",
            Build.Assertion("status", "==", 201),
            Build.Assertion("body", "==", "ann", "user.name"),
            Build.Assertion("headers", "==", "abc123", "x-request-id"),
            Build.Assertion("headers", "starts_with", "application/json", "content-type"),
            Build.Assertion("body_text", "contains", "\"user\""),
            Build.Assertion("body", "contains", "ann"),
            Build.Assertion("duration_ms", ">=", 0));

        var (result, _, _) = await Run(step, _ => Http.Json("{\"user\":{\"name\":\"ann\"}}", HttpStatusCode.Created, ("X-Request-Id", "abc123")));

        Assert.True(result.Passed, string.Join("; ", result.Assertions.Where(a => !a.Passed).Select(a => a.Error)));
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(7, result.Assertions.Count);
    }

    [Fact]
    public async Task A_failing_assertion_fails_the_step_but_it_still_ran()
    {
        var (result, _, _) = await Run(Build.Request("r", "http://x", "GET", Build.Assertion("status", "==", 200)),
            _ => Http.Json("{}", HttpStatusCode.NotFound));

        Assert.False(result.Passed);
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task The_saved_result_exposes_the_body_directly_and_the_response_under_at_http()
    {
        var step = Build.Request("first", "http://x");
        step.SaveAs = "resp";

        var (_, context, _) = await Run(step, _ => Http.Json("{\"status\":\"ok\",\"id\":7}", HttpStatusCode.Accepted, ("X-Trace", "t-1")));

        var variables = context.Variables;
        Assert.Equal("ok", TemplateResolver.ResolveObject("resp.status", variables)?.ToString());         // a body field
        Assert.Equal("202", TemplateResolver.ResolveObject("resp.@http.status", variables)?.ToString());  // the HTTP status
        Assert.Equal("t-1", TemplateResolver.ResolveObject("resp.@http.headers.x-trace", variables));
        Assert.Equal("7", TemplateResolver.ResolveObject("resp.id", variables)?.ToString());
        Assert.Equal(202, variables["first_status"]);
        Assert.IsType<LazyJson>(variables["first_response_json"]);
        Assert.Equal("t-1", TemplateResolver.ResolveObject("first_headers.x-trace", variables));
    }

    [Fact]
    public async Task A_body_that_is_not_json_can_still_be_asserted_and_saved_as_text()
    {
        var step = Build.Request("r", "http://x", "GET",
            Build.Assertion("body", "==", "hello world"),
            Build.Assertion("body", "contains", "hello"));
        step.SaveAs = "plain";

        var (result, context, _) = await Run(step, _ => Http.Text("hello world"));

        Assert.True(result.Passed);
        Assert.Equal("hello world", TemplateResolver.ResolveString("{{plain}}", context.Variables));
    }

    [Fact]
    public async Task A_missing_method_or_url_is_an_error_and_nothing_is_sent()
    {
        var noUrl = Build.Request("r", "");
        var noMethod = Build.Request("r", "http://x");
        noMethod.Method = null;

        var (first, _, firstHandler) = await Run(noUrl);
        var (second, _, secondHandler) = await Run(noMethod);

        Assert.Contains("requires url", first.Error);
        Assert.Contains("requires method", second.Error);
        Assert.Equal(0, firstHandler.Count + secondHandler.Count);
    }

    [Fact]
    public async Task A_connection_failure_is_an_error_result()
    {
        var (result, _, _) = await Run(Build.Request("r", "http://x"), _ => throw new HttpRequestException("connection refused"));

        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("connection refused", result.Error);
    }

    [Fact]
    public async Task An_unresolved_variable_in_the_url_is_an_error_and_nothing_is_sent()
    {
        var (result, _, handler) = await Run(Build.Request("r", "http://x/{{undefined}}"));

        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("undefined", result.Error);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task Secrets_can_be_used_in_headers_and_are_masked_in_the_result()
    {
        var secrets = new RunSecrets(new Dictionary<string, string> { ["token"] = "sk-live-12345" });
        var step = Build.Request("r", "http://x", "GET", Build.Assertion("body", "==", "sk-live-12345", "echo"));
        step.Headers = new(StringComparer.OrdinalIgnoreCase) { ["Authorization"] = "Bearer {{secret.token}}" };

        var (result, context, handler) = await Run(step, _ => Http.Json("{\"echo\":\"nope\"}"), secrets: secrets,
            variables: StepVariables.Initial(Build.Collection(), [], secrets));

        Assert.Equal("Bearer sk-live-12345", handler.Requests[0].Headers["Authorization"]);
        Assert.False(result.Passed);
        Assert.DoesNotContain("sk-live-12345", result.Assertions[0].Expected?.ToString());
        Assert.Contains("********", result.Assertions[0].Expected?.ToString());
        Assert.NotNull(context);
    }
}
