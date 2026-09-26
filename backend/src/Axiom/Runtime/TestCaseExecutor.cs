using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Axiom.Models;

namespace Axiom.Runtime;

public sealed class TestCaseExecutor(HttpClient httpClient, DbQueryExecutor dbQueryExecutor)
{
    public async Task<TestCaseExecutionResult> ExecuteAsync(CollectionDefinition collection, TestCaseDefinition testCase, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var context = BuildInitialContext(collection, testCase);
        var stepResults = new List<StepExecutionResult>(capacity: testCase.Steps.Count);

        foreach (var step in testCase.Steps)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(collection.RunSettings.StepTimeoutSeconds));

            StepExecutionResult result;
            try
            {
                result = step.Type.Trim().ToLowerInvariant() switch
                {
                    "request" => await ExecuteRequestStepAsync(collection, step, context, cts.Token),
                    "db_query" => await ExecuteDbQueryStepAsync(collection, step, context, cts.Token),
                    _ => BuildErrorStep(step, $"Unsupported step type '{step.Type}'"),
                };
            }
            catch (Exception ex)
            {
                result = BuildErrorStep(step, ex.Message);
            }

            stepResults.Add(result);
            if (!result.Passed)
            {
                break;
            }
        }

        return new TestCaseExecutionResult
        {
            Name = testCase.Name,
            SourceFile = testCase.SourceFile,
            Steps = stepResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Dictionary<string, object?> BuildInitialContext(CollectionDefinition collection, TestCaseDefinition testCase)
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in collection.Variables)
        {
            context[pair.Key] = pair.Value;
        }

        foreach (var pair in testCase.Variables)
        {
            context[pair.Key] = pair.Value;
        }

        return context;
    }

    private async Task<StepExecutionResult> ExecuteRequestStepAsync(CollectionDefinition collection, StepDefinition step, Dictionary<string, object?> context, CancellationToken cancellationToken)
    {
        var method = step.Method?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(method))
        {
            return BuildErrorStep(step, "Request step requires method");
        }

        if (string.IsNullOrWhiteSpace(step.Url))
        {
            return BuildErrorStep(step, "Request step requires url");
        }

        var resolvedUrl = AddQueryParameters(step.Url, step.QueryParams, context);
        var request = new HttpRequestMessage(new HttpMethod(method), resolvedUrl);
        ApplyHeaders(request, collection.RequestDefaults.Headers, context);
        ApplyHeaders(request, step.Headers, context);

        if (!string.IsNullOrWhiteSpace(step.Body))
        {
            var body = TemplateResolver.ResolveString(step.Body, context);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var watch = Stopwatch.StartNew();
        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        watch.Stop();

        context[$"{step.Id}_status"] = (int)response.StatusCode;
        context[$"{step.Id}_duration_ms"] = watch.Elapsed.TotalMilliseconds;
        context[$"{step.Id}_response_text"] = responseBody;

        JsonNode? responseJson = null;
        try
        {
            responseJson = JsonNode.Parse(responseBody);
            context[$"{step.Id}_response_json"] = responseJson;
        }
        catch
        {
            // Body is not JSON; text is already available.
        }

        if (!string.IsNullOrWhiteSpace(step.SaveAs))
        {
            context[step.SaveAs] = responseJson ?? responseBody;
        }

        var assertionResults = EvaluateAssertions(step, context, sourceResolver: source => source switch
        {
            "status" => (int)response.StatusCode,
            "duration_ms" => watch.Elapsed.TotalMilliseconds,
            "body" or "response_body" => responseJson ?? responseBody,
            _ => context.TryGetValue(source, out var value) ? value : null,
        });

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name ?? step.Id,
            Assertions = assertionResults,
            Passed = assertionResults.All(a => a.Passed),
            StatusCode = (int)response.StatusCode,
            DurationMs = watch.Elapsed.TotalMilliseconds,
        };
    }

    private static string AddQueryParameters(string url, Dictionary<string, string> queryParams, IReadOnlyDictionary<string, object?> context)
    {
        var resolvedUrl = TemplateResolver.ResolveString(url, context);
        if (queryParams.Count == 0) return resolvedUrl;

        var separator = resolvedUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var values = queryParams.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(TemplateResolver.ResolveString(pair.Value, context))}");
        return resolvedUrl + separator + string.Join('&', values);
    }

    private async Task<StepExecutionResult> ExecuteDbQueryStepAsync(CollectionDefinition collection, StepDefinition step, Dictionary<string, object?> context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.Connection))
        {
            return BuildErrorStep(step, "db_query step requires connection");
        }

        if (string.IsNullOrWhiteSpace(step.Sql))
        {
            return BuildErrorStep(step, "db_query step requires sql");
        }

        if (!collection.Connections.TryGetValue(step.Connection, out var connectionDefinition))
        {
            return BuildErrorStep(step, $"Connection '{step.Connection}' was not found");
        }

        var sql = TemplateResolver.ResolveString(step.Sql, context);
        var watch = Stopwatch.StartNew();
        var rows = await dbQueryExecutor.QueryAsync(connectionDefinition, sql, cancellationToken);
        watch.Stop();

        context[$"{step.Id}_rows"] = rows;
        context[$"{step.Id}_row_count"] = rows.Count;
        if (!string.IsNullOrWhiteSpace(step.SaveAs))
        {
            context[step.SaveAs] = rows;
        }

        var assertionResults = EvaluateAssertions(step, context, sourceResolver: source => source switch
        {
            "row_count" => rows.Count,
            "duration_ms" => watch.Elapsed.TotalMilliseconds,
            "rows" => rows,
            _ => context.TryGetValue(source, out var value) ? value : null,
        });

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name ?? step.Id,
            Assertions = assertionResults,
            Passed = assertionResults.All(a => a.Passed),
            DurationMs = watch.Elapsed.TotalMilliseconds,
            RowCount = rows.Count,
        };
    }

    private static IReadOnlyList<AssertionResult> EvaluateAssertions(StepDefinition step, Dictionary<string, object?> context, Func<string, object?> sourceResolver)
    {
        if (step.Assert.Count == 0)
        {
            return [];
        }

        var results = new List<AssertionResult>(step.Assert.Count);
        foreach (var assertion in step.Assert)
        {
            var sourceValue = sourceResolver(assertion.Source);
            var actual = TemplateResolver.ResolveFrom(sourceValue, assertion.Path);
            results.Add(AssertionEngine.Evaluate(assertion, actual, context));
        }

        return results;
    }

    private static StepExecutionResult BuildErrorStep(StepDefinition step, string message)
    {
        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name ?? step.Id,
            Assertions = [],
            Passed = false,
            Error = message,
        };
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string> headers, IReadOnlyDictionary<string, object?> context)
    {
        foreach (var header in headers)
        {
            var value = TemplateResolver.ResolveString(header.Value, context);

            if (!request.Headers.TryAddWithoutValidation(header.Key, value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.Remove(header.Key);
                request.Content.Headers.TryAddWithoutValidation(header.Key, value);
            }
        }

        // Provide default accept header for API tests unless user overrides it.
        if (request.Headers.Accept.Count == 0)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }
}