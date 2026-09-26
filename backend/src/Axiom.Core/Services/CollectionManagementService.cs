using Axiom.Documents;
using Axiom.Models;
using Axiom.Parsing;
using Axiom.Runtime;
using Axiom.Serialization;
using System.Text;
using System.Text.Json;

namespace Axiom.Services;

public sealed class CollectionManagementService
{
    public CollectionDocument? GetCollection(string folderPath)
    {
        var path = CollectionPaths.CollectionFile(folderPath);
        if (!File.Exists(path))
        {
            return null;
        }

        var document = DeserializeFile<CollectionDocument>(path) ?? new CollectionDocument();
        document.Variables ??= new(StringComparer.OrdinalIgnoreCase);
        document.Connections ??= new(StringComparer.OrdinalIgnoreCase);
        document.Secrets ??= new(StringComparer.OrdinalIgnoreCase);
        document.RunSettings ??= new RunSettingsDocument();
        document.RequestDefaults ??= new RequestDefaultsDocument();
        document.RequestDefaults.Headers ??= new(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    public void SaveCollectionVariables(string folderPath, Dictionary<string, object?> variables)
    {
        var collection = GetCollection(folderPath) ?? throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found");
        collection.Variables = NormalizeDictionary(variables);
        SerializeFile(CollectionPaths.CollectionFile(folderPath), collection);
    }

    public void SaveCollectionSettings(string folderPath, Dictionary<string, object?> variables, Dictionary<string, object?> connections, Dictionary<string, SecretReference>? secrets = null)
    {
        var collection = GetCollection(folderPath) ?? throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found");
        collection.Variables = NormalizeDictionary(variables);
        collection.Connections = NormalizeDictionary(connections);
        if (secrets is not null)
        {
            collection.Secrets = new Dictionary<string, SecretReference>(secrets, StringComparer.OrdinalIgnoreCase);
        }
        SerializeFile(CollectionPaths.CollectionFile(folderPath), collection);
    }

    public void SaveCollectionSecrets(string folderPath, Dictionary<string, SecretReference> secrets)
    {
        var collection = GetCollection(folderPath) ?? throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found");
        collection.Secrets = new Dictionary<string, SecretReference>(secrets, StringComparer.OrdinalIgnoreCase);
        SerializeFile(CollectionPaths.CollectionFile(folderPath), collection);
    }

    public IReadOnlyList<TestCaseListItem> ListTests(string folderPath)
    {
        var testsDir = CollectionPaths.TestsDirectory(folderPath);
        if (!Directory.Exists(testsDir))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(testsDir, CollectionPaths.TestFilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var test = DeserializeFile<TestCaseDocument>(path);
                var id = CollectionPaths.ToTestId(fileName);
                return new TestCaseListItem
                {
                    FileName = fileName,
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(test?.Name) ? ToDisplayName(id) : test.Name,
                    Endpoint = test?.Endpoint ?? test?.Steps.FirstOrDefault(x => string.Equals(x.Type, RequestStepExecutor.StepType, StringComparison.OrdinalIgnoreCase))?.Url,
                    Method = test?.Method ?? test?.Steps.FirstOrDefault(x => string.Equals(x.Type, RequestStepExecutor.StepType, StringComparison.OrdinalIgnoreCase))?.Method,
                };
            })
            .ToList();
    }

    public TestCaseDocument? GetTest(string folderPath, string fileName)
    {
        var path = CollectionPaths.TestFile(folderPath, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var document = DeserializeFile<TestCaseDocument>(path) ?? new TestCaseDocument();
        document.Variables ??= new(StringComparer.OrdinalIgnoreCase);
        document.Steps ??= [];
        foreach (var step in document.Steps)
        {
            step.Headers ??= new(StringComparer.OrdinalIgnoreCase);
            step.QueryParams ??= new(StringComparer.OrdinalIgnoreCase);
            step.Assert ??= [];
        }

        return document;
    }

    /// <summary>
    /// Saves a test. With <see cref="SaveTestCaseRequest.FileName"/> of an existing file, that file is updated
    /// (and renamed to follow a changed test name, unless its name was customised). Otherwise a new file is
    /// created with a unique, name-derived file name; an existing test is never overwritten by accident.
    /// </summary>
    public (string FilePath, string FileName) SaveTest(string folderPath, SaveTestCaseRequest request)
    {
        var testsDirectory = CollectionPaths.TestsDirectory(folderPath);
        Directory.CreateDirectory(testsDirectory);

        var currentName = string.IsNullOrWhiteSpace(request.FileName)
            ? null
            : CollectionPaths.ToTestFileName(request.FileName.Trim());
        var currentPath = currentName is null ? null : CollectionPaths.TestFile(folderPath, currentName);
        var updating = currentPath is not null && File.Exists(currentPath);

        var targetName = updating
            ? ResolveNameForUpdate(folderPath, currentName!, currentPath!, request.Name)
            : UniqueFileName(folderPath, request.FileNameHint ?? (currentName is null ? request.Name : CollectionPaths.ToTestId(currentName)), excluding: null);
        var targetPath = CollectionPaths.TestFile(folderPath, targetName);

        var document = new TestCaseDocument
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Endpoint = request.Endpoint,
            Method = request.Method,
            Variables = NormalizeDictionary(request.Variables),
            Steps = NormalizeSteps(request.Steps),
        };

        SerializeFile(targetPath, document);
        if (updating && !string.Equals(targetName, currentName, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(currentPath!);
        }

        return (targetPath, targetName);
    }

    public bool TestExists(string folderPath, string fileNameOrId) =>
        File.Exists(CollectionPaths.TestFile(folderPath, fileNameOrId));

    /// <summary>Keeps the file name in step with the test name, but only if the file still has the name Axiom gave it.</summary>
    private string ResolveNameForUpdate(string folderPath, string currentName, string currentPath, string newTestName)
    {
        var oldTestName = DeserializeFile<TestCaseDocument>(currentPath)?.Name;
        var currentId = CollectionPaths.ToTestId(currentName);
        var followsName = TestFileNames.IsGeneratedFrom(currentId, oldTestName);
        var alreadyMatches = TestFileNames.IsGeneratedFrom(currentId, newTestName);
        return followsName && !alreadyMatches
            ? UniqueFileName(folderPath, newTestName, excluding: currentPath)
            : currentName;
    }

    private static string UniqueFileName(string folderPath, string source, string? excluding)
    {
        var slug = TestFileNames.Slug(source);
        var candidate = slug;
        for (var number = 2; IsTaken(folderPath, candidate, excluding); number++)
        {
            candidate = $"{slug}-{number}";
        }

        return CollectionPaths.ToTestFileName(candidate);
    }

    private static bool IsTaken(string folderPath, string id, string? excluding)
    {
        var path = CollectionPaths.TestFile(folderPath, id);
        return File.Exists(path) && !string.Equals(path, excluding, StringComparison.OrdinalIgnoreCase);
    }

    public void DeleteTest(string folderPath, string fileName)
    {
        var path = CollectionPaths.TestFile(folderPath, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static T? DeserializeFile<T>(string path)
    {
        var content = File.ReadAllText(path);
        return YamlSerialization.Deserializer.Deserialize<T>(content);
    }

    private static void SerializeFile(string path, object document)
    {
        var content = YamlSerialization.Serializer.Serialize(document);
        File.WriteAllText(path, content);
    }

    private static string ToDisplayName(string value)
    {
        var words = value.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static Dictionary<string, object?> NormalizeDictionary(Dictionary<string, object?>? values)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var (key, value) in values)
        {
            result[key] = NormalizeValue(value);
        }

        return result;
    }

    private static List<StepDocument> NormalizeSteps(List<StepDocument>? steps)
    {
        if (steps is null)
        {
            return [];
        }

        return steps.Select(step => new StepDocument
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name,
            Method = step.Method,
            Url = step.Url,
            QueryParams = step.QueryParams is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(step.QueryParams, StringComparer.OrdinalIgnoreCase),
            Headers = step.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(step.Headers, StringComparer.OrdinalIgnoreCase),
            Body = step.Body,
            Connection = step.Connection,
            Sql = step.Sql,
            SaveAs = step.SaveAs,
            Assert = step.Assert?.Select(assertion => new AssertionDocument
            {
                Source = assertion.Source,
                Path = assertion.Path,
                Aggregate = string.IsNullOrWhiteSpace(assertion.Aggregate) ? null : assertion.Aggregate.Trim().ToLowerInvariant(),
                Operator = assertion.Operator,
                Expected = NormalizeValue(assertion.Expected),
            }).ToList() ?? [],
        }).ToList();
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement json)
        {
            return NormalizeJsonElement(json);
        }

        return value;
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return NormalizeNumber(element);
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => NormalizeJsonElement(p.Value),
                StringComparer.OrdinalIgnoreCase),
            _ => element.ToString(),
        };
    }

    private static object NormalizeNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var i64))
        {
            return i64;
        }

        if (element.TryGetDecimal(out var dec))
        {
            return dec;
        }

        return element.GetDouble();
    }
}
