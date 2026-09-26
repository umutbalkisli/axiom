using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Axiom.Models;

namespace Axiom.Runtime;

public sealed class RequestStepExecutor(HttpClient httpClient, AssertionEngine assertionEngine) : IStepExecutor
{
    public const string StepType = "request";

    public string Type => StepType;

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

        if (!string.IsNullOrWhiteSpace(step.Body))
        {
            var body = TemplateResolver.ResolveString(step.Body, variables);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        ApplyHeaders(request, context.Collection.RequestDefaults.Headers, variables);
        ApplyHeaders(request, step.Headers, variables);

        // Provide default accept header for API tests unless user overrides it.
        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        var watch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        watch.Stop();

        var statusCode = (int)response.StatusCode;
        variables[$"{step.Id}_status"] = statusCode;
        variables[$"{step.Id}_duration_ms"] = watch.Elapsed.TotalMilliseconds;
        variables[$"{step.Id}_response_text"] = responseBody;

        JsonNode? responseJson = null;
        try
        {
            responseJson = JsonNode.Parse(responseBody);
            variables[$"{step.Id}_response_json"] = responseJson;
        }
        catch
        {
            // Body is not JSON; text is already available.
        }

        if (!string.IsNullOrWhiteSpace(step.SaveAs))
        {
            variables[step.SaveAs] = responseJson ?? responseBody;
        }

        var assertionResults = assertionEngine.EvaluateAll(step, variables, source => source switch
        {
            "status" => statusCode,
            "duration_ms" => watch.Elapsed.TotalMilliseconds,
            "body" or "response_body" => responseJson ?? responseBody,
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
