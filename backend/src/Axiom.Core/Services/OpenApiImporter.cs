using Axiom.Documents;
using Axiom.Network;
using Axiom.Parsing;
using Axiom.Runtime;
using Axiom.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Services;

/// <summary>
/// Creates one starter test per operation of an OpenAPI document.
/// </summary>
/// <summary>
/// Creates the importer.
/// </summary>
public sealed class OpenApiImporter(HttpClient httpClient, CollectionInitializer initializer, CollectionManagementService manager, NetworkSettings? network = null)
{
    /// <summary>
    /// Downloads an OpenAPI document and saves one basic test per operation. Returns the number of tests created.
    /// </summary>
    public async Task<int> ImportFromUrlAsync(string folderPath, string collectionName, string specificationUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(specificationUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("OpenAPI URL must be a valid HTTP or HTTPS URL.");
        }

        string specification;
        try
        {
            specification = await httpClient.GetStringAsync(uri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"The OpenAPI document could not be downloaded: {NetworkErrors.Describe(ex, uri, network ?? new NetworkSettings())}", ex);
        }

        var extension = uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ".json" : ".yaml";
        await initializer.InitializeAsync(folderPath, collectionName, cancellationToken);
        return ImportOpenApiDocument(folderPath, extension, specification);
    }

    private int ImportOpenApiDocument(string folderPath, string extension, string specification)
    {
        var document = ParseOpenApi(extension, specification);
        if (document is null)
        {
            throw new InvalidOperationException("The OpenAPI document could not be parsed.");
        }

        var paths = document["paths"] as JsonObject;
        if (paths is null) return 0;

        var imported = 0;
        foreach (var path in paths)
        {
            if (path.Value is not JsonObject operations) continue;
            foreach (var operation in operations)
            {
                if (!IsHttpMethod(operation.Key) || operation.Value is not JsonObject details) continue;
                var method = operation.Key.ToUpperInvariant();
                var operationId = details["operationId"]?.GetValue<string>() ?? $"{method.ToLowerInvariant()}-{path.Key.Trim('/').Replace('/', '-') }";
                var summary = details["summary"]?.GetValue<string>() ?? details["description"]?.GetValue<string>() ?? $"{method} {path.Key}";
                // Re-importing must not overwrite tests that were already imported (and maybe edited).
                if (manager.TestExists(folderPath, TestFileNames.Slug(operationId)))
                {
                    continue;
                }

                manager.SaveTest(folderPath, new SaveTestCaseRequest
                {
                    FileNameHint = operationId,
                    Name = summary,
                    Description = details["description"]?.GetValue<string>() ?? string.Empty,
                    Endpoint = path.Key,
                    Method = method,
                    Steps =
                    [
                        new StepDocument
                        {
                            Id = "request_1",
                            Type = RequestStepExecutor.StepType,
                            Name = summary,
                            Method = method,
                            Url = $"{{{{base_url}}}}{path.Key}",
                            Assert = [new AssertionDocument { Source = "status", Operator = "==", Expected = 200 }],
                        },
                    ],
                });
                imported++;
            }
        }

        return imported;
    }

    private static JsonObject? ParseOpenApi(string extension, string content)
    {
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonNode.Parse(content)?.AsObject();
        }

        var raw = YamlSerialization.Deserializer.Deserialize<object>(content);
        return JsonSerializer.SerializeToNode(NormalizeYamlValue(raw))?.AsObject();
    }

    private static object? NormalizeYamlValue(object? value)
    {
        if (value is IDictionary<object, object> map)
        {
            return map.ToDictionary(pair => pair.Key?.ToString() ?? string.Empty, pair => NormalizeYamlValue(pair.Value));
        }

        if (value is System.Collections.IEnumerable sequence and not string)
        {
            return sequence.Cast<object?>().Select(NormalizeYamlValue).ToList();
        }

        return value;
    }

    private static bool IsHttpMethod(string value) => value is "get" or "post" or "put" or "patch" or "delete" or "head" or "options";
}
