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
        var document = new TestCaseDocument
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Endpoint = request.Endpoint,
            Method = request.Method,
            Variables = NormalizeDictionary(request.Variables),
            Steps = NormalizeSteps(request.Steps),
        };

        return SaveNamed(folderPath, CollectionPaths.Tests, request.FileName, request.FileNameHint, request.Name, document, followName: true);
    }

    public bool TestExists(string folderPath, string fileNameOrId) =>
        File.Exists(CollectionPaths.TestFile(folderPath, fileNameOrId));

    public IReadOnlyList<SharedStepsListItem> ListShared(string folderPath)
    {
        var directory = CollectionPaths.Directory(folderPath, CollectionPaths.Shared);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var all = ReadAllShared(folderPath);
        return all
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new SharedStepsListItem
            {
                FileName = CollectionPaths.ToFileName(CollectionPaths.Shared, pair.Key),
                Id = pair.Key,
                Name = string.IsNullOrWhiteSpace(pair.Value.Name) ? ToDisplayName(pair.Key) : pair.Value.Name,
                Description = pair.Value.Description,
                Run = pair.Value.Run,
                StepCount = pair.Value.Steps.Count,
                Provides = ProvidedNames(pair.Value, all, [pair.Key]),
            })
            .ToList();
    }

    public SharedStepsDocument? GetShared(string folderPath, string fileName)
    {
        var path = CollectionPaths.File(folderPath, CollectionPaths.Shared, fileName);
        return File.Exists(path) ? ReadShared(path) : null;
    }

    /// <summary>Saves a shared step group. Its file name is fixed once created because tests refer to it by that name.</summary>
    public (string FilePath, string FileName) SaveShared(string folderPath, SaveSharedStepsRequest request)
    {
        var document = new SharedStepsDocument
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Run = string.Equals(request.Run?.Trim(), SharedStepsDefinition.RunOnce, StringComparison.OrdinalIgnoreCase)
                ? SharedStepsDefinition.RunOnce
                : SharedStepsDefinition.RunEach,
            Steps = NormalizeSteps(request.Steps),
        };

        return SaveNamed(folderPath, CollectionPaths.Shared, request.FileName, null, request.Name, document, followName: false);
    }

    /// <summary>Deletes a shared group unless a test or another group still includes it.</summary>
    public void DeleteShared(string folderPath, string fileName)
    {
        var path = CollectionPaths.File(folderPath, CollectionPaths.Shared, fileName);
        var id = CollectionPaths.ToId(CollectionPaths.Shared, Path.GetFileName(path));

        var users = FindIncludingFiles(folderPath, id);
        if (users.Count > 0)
        {
            throw new InvalidOperationException($"'{id}' is still used by: {string.Join(", ", users)}.");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private List<string> FindIncludingFiles(string folderPath, string sharedId)
    {
        bool Includes(IEnumerable<StepDocument> steps) => steps.Any(step =>
            string.Equals(step.Type, IncludeStepExecutor.StepType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(step.Ref?.Trim(), sharedId, StringComparison.OrdinalIgnoreCase));

        var users = new List<string>();
        var testsDirectory = CollectionPaths.TestsDirectory(folderPath);
        if (Directory.Exists(testsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(testsDirectory, CollectionPaths.TestFilePattern, SearchOption.TopDirectoryOnly))
            {
                if (Includes(DeserializeFile<TestCaseDocument>(path)?.Steps ?? []))
                {
                    users.Add($"tests/{Path.GetFileName(path)}");
                }
            }
        }

        foreach (var (id, document) in ReadAllShared(folderPath))
        {
            if (!string.Equals(id, sharedId, StringComparison.OrdinalIgnoreCase) && Includes(document.Steps))
            {
                users.Add($"shared/{CollectionPaths.ToFileName(CollectionPaths.Shared, id)}");
            }
        }

        return users;
    }

    private Dictionary<string, SharedStepsDocument> ReadAllShared(string folderPath)
    {
        var result = new Dictionary<string, SharedStepsDocument>(StringComparer.OrdinalIgnoreCase);
        var directory = CollectionPaths.Directory(folderPath, CollectionPaths.Shared);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(directory, CollectionPaths.Shared.Pattern, SearchOption.TopDirectoryOnly))
        {
            result[CollectionPaths.ToId(CollectionPaths.Shared, Path.GetFileName(path))] = ReadShared(path);
        }

        return result;
    }

    private static SharedStepsDocument ReadShared(string path)
    {
        var document = DeserializeFile<SharedStepsDocument>(path) ?? new SharedStepsDocument();
        document.Steps ??= [];
        foreach (var step in document.Steps)
        {
            step.Headers ??= new(StringComparer.OrdinalIgnoreCase);
            step.QueryParams ??= new(StringComparer.OrdinalIgnoreCase);
            step.Assert ??= [];
        }

        return document;
    }

    /// <summary>The variables a group saves, including those saved by groups it includes.</summary>
    private static List<string> ProvidedNames(SharedStepsDocument document, Dictionary<string, SharedStepsDocument> all, HashSet<string> visiting)
    {
        var names = new List<string>();
        foreach (var step in document.Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.SaveAs))
            {
                names.Add(step.SaveAs);
            }

            var reference = step.Ref?.Trim();
            if (string.Equals(step.Type, IncludeStepExecutor.StepType, StringComparison.OrdinalIgnoreCase)
                && reference is { Length: > 0 }
                && all.TryGetValue(reference, out var included)
                && visiting.Add(reference))
            {
                names.AddRange(ProvidedNames(included, all, visiting));
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Writes a document to a file of the given kind. An existing <paramref name="requestedFile"/> is updated in place
    /// (or renamed to follow <paramref name="name"/> when <paramref name="followName"/> and the name was generated);
    /// otherwise a new file with a unique name is created.
    /// </summary>
    private (string FilePath, string FileName) SaveNamed(string folderPath, FileKind kind, string? requestedFile, string? hint, string name, object document, bool followName)
    {
        Directory.CreateDirectory(CollectionPaths.Directory(folderPath, kind));

        var currentName = string.IsNullOrWhiteSpace(requestedFile)
            ? null
            : CollectionPaths.ToFileName(kind, requestedFile.Trim());
        var currentPath = currentName is null ? null : CollectionPaths.File(folderPath, kind, currentName);
        var updating = currentPath is not null && File.Exists(currentPath);

        var targetName = updating
            ? (followName ? ResolveNameForUpdate(folderPath, kind, currentName!, currentPath!, name) : currentName!)
            : UniqueFileName(folderPath, kind, hint ?? (currentName is null ? name : CollectionPaths.ToId(kind, currentName)), excluding: null);
        var targetPath = CollectionPaths.File(folderPath, kind, targetName);

        SerializeFile(targetPath, document);
        if (updating && !string.Equals(targetName, currentName, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(currentPath!);
        }

        return (targetPath, targetName);
    }

    /// <summary>Keeps the file name in step with the name, but only if the file still has the name Axiom gave it.</summary>
    private static string ResolveNameForUpdate(string folderPath, FileKind kind, string currentName, string currentPath, string newName)
    {
        var oldName = DeserializeFile<NamedDocument>(currentPath)?.Name;
        var currentId = CollectionPaths.ToId(kind, currentName);
        var followsName = TestFileNames.IsGeneratedFrom(currentId, oldName);
        var alreadyMatches = TestFileNames.IsGeneratedFrom(currentId, newName);
        return followsName && !alreadyMatches
            ? UniqueFileName(folderPath, kind, newName, excluding: currentPath)
            : currentName;
    }

    private static string UniqueFileName(string folderPath, FileKind kind, string source, string? excluding)
    {
        var slug = TestFileNames.Slug(source);
        var candidate = slug;
        for (var number = 2; IsTaken(folderPath, kind, candidate, excluding); number++)
        {
            candidate = $"{slug}-{number}";
        }

        return CollectionPaths.ToFileName(kind, candidate);
    }

    private static bool IsTaken(string folderPath, FileKind kind, string id, string? excluding)
    {
        var path = CollectionPaths.File(folderPath, kind, id);
        return File.Exists(path) && !string.Equals(path, excluding, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NamedDocument
    {
        public string? Name { get; set; }
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
            Ref = string.IsNullOrWhiteSpace(step.Ref) ? null : step.Ref.Trim(),
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
