using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Creates the executor.
/// </summary>
/// <summary>
/// Runs a <c>request</c> step: sends an HTTP request and checks assertions on the response.
/// </summary>
public sealed class RequestStepExecutor(HttpClient httpClient, AssertionEngine assertionEngine) : IStepExecutor
{
    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public const string StepType = "request";

    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public string Type => StepType;

    /// <summary>
    /// Sends the request and evaluates the step's assertions on <c>status</c>, <c>body</c>, <c>body_text</c>, <c>headers</c> and <c>duration_ms</c>.
    /// </summary>
    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken)
    {
        var variables = context.Variables;

        var method = step.Method?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(method))
        {
            return StepResults.Error(step, "Request step requires method");
        }

        if (string.IsNullOrWhiteSpace(step.Url))
        {
            return StepResults.Error(step, "Request step requires url");
        }

        var resolvedUrl = AddQueryParameters(step.Url, step.QueryParams, variables);
        using var request = new HttpRequestMessage(new HttpMethod(method), resolvedUrl);

        string? body = null;
        if (!string.IsNullOrWhiteSpace(step.Body))
        {
            body = TemplateResolver.ResolveString(step.Body, variables);
            // Only claim JSON when it is JSON: a server handed invalid JSON as application/json typically fails with a 500
            // that hides the real mistake. A Content-Type header on the step or in request_defaults still wins.
            request.Content = new StringContent(body, Encoding.UTF8, IsJson(body) ? "application/json" : "text/plain");
        }

        ApplyHeaders(request, context.Collection.RequestDefaults.Headers, variables);
        ApplyHeaders(request, step.Headers, variables);

        // Provide default accept header for API tests unless user overrides it.
        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // Recorded before sending, so it is there to look at even when no response comes back.
        variables[$"{step.Id}_request"] = DescribeRequest(request, body);

        var watch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        watch.Stop();

        var statusCode = (int)response.StatusCode;
        variables[$"{step.Id}_status"] = statusCode;
        variables[$"{step.Id}_duration_ms"] = watch.Elapsed.TotalMilliseconds;
        variables[$"{step.Id}_response_text"] = responseBody;

        // Response and content headers together, looked up case-insensitively (headers.content-type).
        var responseHeaders = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }

        variables[$"{step.Id}_headers"] = responseHeaders;

        // The body is only parsed as JSON if an assertion or a later step reads it.
        var responseJson = new LazyJson(responseBody, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = statusCode,
            ["duration_ms"] = watch.Elapsed.TotalMilliseconds,
            ["headers"] = responseHeaders,
        });
        variables[$"{step.Id}_response_json"] = responseJson;

        if (!string.IsNullOrWhiteSpace(step.SaveAs))
        {
            variables[step.SaveAs] = responseJson;
        }

        var assertionResults = assertionEngine.EvaluateAll(step, variables, source => source switch
        {
            "status" => statusCode,
            "duration_ms" => watch.Elapsed.TotalMilliseconds,
            "body" or "response_body" => responseJson,
            "body_text" => responseBody,
            "headers" => responseHeaders,
            _ => variables.TryGetValue(source, out var value) ? value : null,
        });

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name ?? step.Id,
            Assertions = assertionResults,
            Passed = assertionResults.All(a => a.Passed),
            StatusCode = statusCode,
            DurationMs = watch.Elapsed.TotalMilliseconds,
        };
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The request as it goes out: method, URL, every header (request and content) and body.
    /// </summary>
    private static Dictionary<string, object?> DescribeRequest(HttpRequestMessage request, string? body)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["method"] = request.Method.Method,
            ["url"] = request.RequestUri?.ToString() ?? string.Empty,
            ["headers"] = headers,
            ["body"] = body,
        };
    }

    private static string AddQueryParameters(string url, Dictionary<string, string> queryParams, IReadOnlyDictionary<string, object?> variables)
    {
        var resolvedUrl = TemplateResolver.ResolveString(url, variables);
        if (queryParams.Count == 0) return resolvedUrl;

        var separator = resolvedUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var values = queryParams.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(TemplateResolver.ResolveString(pair.Value, variables))}");
        return resolvedUrl + separator + string.Join('&', values);
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string> headers, IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var header in headers)
        {
            var value = TemplateResolver.ResolveString(header.Value, variables);

            if (!request.Headers.TryAddWithoutValidation(header.Key, value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.Remove(header.Key);
                request.Content.Headers.TryAddWithoutValidation(header.Key, value);
            }
        }
    }
}
