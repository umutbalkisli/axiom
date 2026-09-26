using Axiom.Documents;
using Axiom.Parsing;
using Axiom.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Axiom.Services;

public sealed class AxiomService
{
    private readonly CollectionRunner _runner = new(new YamlCollectionLoader());

    public Task<CollectionExecutionResult> RunCollectionAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        return _runner.RunAsync(folderPath, cancellationToken);
    }

    public static async Task InitializeCollectionAsync(string folderPath, string collectionName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "tests"));

        var collectionYamlPath = Path.Combine(folderPath, "collection.yaml");
        if (!File.Exists(collectionYamlPath))
        {
            var content = string.Join(
                Environment.NewLine,
                $"name: {collectionName}",
                "description: New API test collection",
                "connections: {}",
                "variables:",
                "  base_url: https://api.example.com",
                "run_settings:",
                "  max_parallel_test_cases: 4",
                "  step_timeout_seconds: 30",
                "request_defaults:",
                "  headers:",
                "    Accept: application/json");
            await File.WriteAllTextAsync(collectionYamlPath, content, cancellationToken);
        }
    }

    public static async Task<int> ImportOpenApiAsync(string folderPath, string collectionName, string specificationPath, CancellationToken cancellationToken = default)
    {
        await InitializeCollectionAsync(folderPath, collectionName, cancellationToken);
        var specification = await File.ReadAllTextAsync(specificationPath, cancellationToken);
        return ImportOpenApiDocument(folderPath, Path.GetExtension(specificationPath), specification);
    }

    public static async Task<int> ImportOpenApiFromUrlAsync(string folderPath, string collectionName, string specificationUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(specificationUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("OpenAPI URL must be a valid HTTP or HTTPS URL.");
        }

        using var httpClient = new HttpClient();
        var specification = await httpClient.GetStringAsync(uri, cancellationToken);
        var extension = uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ".json" : ".yaml";
        await InitializeCollectionAsync(folderPath, collectionName, cancellationToken);
        return ImportOpenApiDocument(folderPath, extension, specification);
    }

    private static int ImportOpenApiDocument(string folderPath, string extension, string specification)
    {
        var document = ParseOpenApi(extension, specification);
        if (document is null)
        {
            throw new InvalidOperationException("The OpenAPI document could not be parsed.");
        }

        var manager = new CollectionManagementService();
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
                manager.SaveTest(folderPath, new SaveTestCaseRequest
                {
                    FileName = operationId,
                    Name = summary,
                    Description = details["description"]?.GetValue<string>() ?? string.Empty,
                    Endpoint = path.Key,
                    Method = method,
                    Steps =
                    [
                        new StepDocument
                        {
                            Id = "request_1",
                            Type = "request",
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

        var yaml = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var raw = yaml.Deserialize<object>(content);
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
