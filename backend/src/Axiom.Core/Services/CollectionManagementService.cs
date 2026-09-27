using Axiom.Documents;
using Axiom.Models;
using Axiom.Parsing;
using Axiom.Runtime;
using Axiom.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Axiom.Services;

/// <summary>
/// Reads and writes the editable parts of a collection: settings, tests and shared steps.
/// </summary>
public sealed partial class CollectionManagementService
{
    /// <summary>
    /// Reads <c>collection.yaml</c>; null when the folder has none.
    /// </summary>
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

    /// <summary>
    /// Replaces the collection variables.
    /// </summary>
    public void SaveCollectionVariables(string folderPath, Dictionary<string, object?> variables)
    {
        var collection = GetCollection(folderPath) ?? throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found");
        collection.Variables = NormalizeDictionary(variables);
        SerializeFile(CollectionPaths.CollectionFile(folderPath), collection);
    }

    /// <summary>
    /// Replaces the collection variables and connections, and the secret references when given.
    /// </summary>
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

    /// <summary>
    /// Replaces the secret references.
    /// </summary>
    public void SaveCollectionSecrets(string folderPath, Dictionary<string, SecretReference> secrets)
    {
        var collection = GetCollection(folderPath) ?? throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found");
        collection.Secrets = new Dictionary<string, SecretReference>(secrets, StringComparer.OrdinalIgnoreCase);
        SerializeFile(CollectionPaths.CollectionFile(folderPath), collection);
    }

    /// <summary>
    /// Lists the tests in the tests folder and its subfolders, each with its path relative to the tests folder.
    /// </summary>
    public IReadOnlyList<TestCaseListItem> ListTests(string folderPath)
    {
        var testsDir = CollectionPaths.TestsDirectory(folderPath);
        if (!Directory.Exists(testsDir))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(testsDir, CollectionPaths.TestFilePattern, SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: TestPaths.RelativePath(folderPath, path)))
            .OrderBy(file => file.Relative, StringComparer.OrdinalIgnoreCase)
            .Select(file =>
            {
                var test = DeserializeFile<TestCaseDocument>(file.Path);
                var id = CollectionPaths.ToTestId(file.Relative);
                var firstRequest = test?.Steps.FirstOrDefault(x => string.Equals(x.Type, RequestStepExecutor.StepType, StringComparison.OrdinalIgnoreCase));
                return new TestCaseListItem
                {
                    FileName = file.Relative,
                    Id = id,
                    Folder = TestPaths.FolderOf(file.Relative),
                    TestId = string.IsNullOrWhiteSpace(test?.Id) ? null : test.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(test?.Name) ? ToDisplayName(TestPaths.FileNameOf(id)) : test.Name,
                    Endpoint = test?.Endpoint ?? firstRequest?.Url,
                    Method = test?.Method ?? firstRequest?.Method,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Reads one test; null when it does not exist.
    /// </summary>
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
        var document = ToDocument(request);
        var relative = string.IsNullOrWhiteSpace(request.FileName) ? null : TestPaths.Normalize(request.FileName);
        var existingPath = relative is null ? null : CollectionPaths.TestFile(folderPath, relative);
        var updating = existingPath is not null && File.Exists(existingPath);

        // The id is the test's identity: kept when updating (given now to a test written before ids existed), new otherwise.
        document.Id = updating
            ? DeserializeFile<NamedDocument>(existingPath!)?.Id?.Trim() is { Length: > 0 } kept ? kept : NewTestId()
            : NewTestId();

        var folder = relative is not null ? TestPaths.FolderOf(relative) : TestPaths.FolderForNewName(folderPath, request.Folder);
        var directory = TestPaths.FullPath(folderPath, folder, isFolder: true);
        var fileName = SaveInDirectory(directory, CollectionPaths.TestFileSuffix, relative is null ? null : TestPaths.FileNameOf(relative), request.FileNameHint, request.Name, document, followName: true);
        return (Path.Combine(directory, fileName), TestPaths.Combine(folder, fileName));
    }

    /// <summary>
    /// The YAML a test would be saved as. Lets an unsaved test be run exactly as it will run once saved.
    /// </summary>
    public static string ToYaml(SaveTestCaseRequest request) => YamlSerialization.Serializer.Serialize(ToDocument(request));

    private static TestCaseDocument ToDocument(SaveTestCaseRequest request) => new()
    {
        Name = request.Name,
        Description = request.Description ?? string.Empty,
        Endpoint = request.Endpoint,
        Method = request.Method,
        Variables = NormalizeDictionary(request.Variables),
        Steps = NormalizeSteps(request.Steps),
    };

    /// <summary>
    /// True when the test file exists.
    /// </summary>
    public bool TestExists(string folderPath, string fileNameOrId) =>
        File.Exists(CollectionPaths.TestFile(folderPath, fileNameOrId));

    /// <summary>
    /// Lists the shared step groups, including the variables each provides.
    /// </summary>
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

    /// <summary>
    /// Reads one shared group; null when it does not exist.
    /// </summary>
    public SharedStepsDocument? GetShared(string folderPath, string fileName)
    {
        var path = CollectionPaths.File(folderPath, CollectionPaths.Shared, fileName);
        return File.Exists(path) ? ReadShared(path) : null;
    }

    /// <summary>
    /// Saves a shared step group. Its file name is fixed once created because tests refer to it by that name.
    /// </summary>
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

    /// <summary>
    /// Deletes a shared group unless a test or another group still includes it.
    /// </summary>
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
            foreach (var path in Directory.EnumerateFiles(testsDirectory, CollectionPaths.TestFilePattern, SearchOption.AllDirectories))
            {
                if (Includes(DeserializeFile<TestCaseDocument>(path)?.Steps ?? []))
                {
                    users.Add($"tests/{TestPaths.RelativePath(folderPath, path)}");
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

    /// <summary>
    /// The variables a group saves, including those saved by groups it includes.
    /// </summary>
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
    /// Writes a shared step group. An existing <paramref name="requestedFile"/> is updated in place; otherwise a new
    /// file with a unique name is created. Nothing is ever overwritten.
    /// </summary>
    private static (string FilePath, string FileName) SaveNamed(string folderPath, FileKind kind, string? requestedFile, string? hint, string name, object document, bool followName)
    {
        var directory = CollectionPaths.Directory(folderPath, kind);
        var currentName = string.IsNullOrWhiteSpace(requestedFile) ? null : CollectionPaths.ToFileName(kind, requestedFile.Trim());
        if (currentName is not null)
        {
            // Validates the name (no path separators) before anything is written.
            CollectionPaths.File(folderPath, kind, currentName);
        }

        var fileName = SaveInDirectory(directory, kind.Suffix, currentName, hint, name, document, followName);
        return (Path.Combine(directory, fileName), fileName);
    }

    /// <summary>
    /// Writes <paramref name="document"/> into <paramref name="directory"/> and returns its file name. An existing
    /// <paramref name="currentName"/> is written in place first and then, with <paramref name="followName"/>, renamed to
    /// follow a changed <paramref name="name"/> (only while its file name is still the one Axiom generated). A new file
    /// gets a unique name. The file system refuses any overwrite (see <see cref="SafeFiles"/>).
    /// </summary>
    private static string SaveInDirectory(string directory, string suffix, string? currentName, string? hint, string name, object document, bool followName)
    {
        var currentPath = currentName is null ? null : Path.Combine(directory, currentName);
        if (currentPath is not null && File.Exists(currentPath))
        {
            var currentId = currentName![..^suffix.Length];
            var oldName = DeserializeFile<NamedDocument>(currentPath)?.Name;
            var rename = followName
                && TestFileNames.IsGeneratedFrom(currentId, oldName)
                && !TestFileNames.IsGeneratedFrom(currentId, name);

            // Content first, in place: if the rename below fails, the edit is still saved under the old name.
            SerializeFile(currentPath, document);
            return rename ? SafeFiles.MoveUnique(currentPath, directory, TestFileNames.Slug(name), suffix) : currentName;
        }

        var source = hint ?? (currentName is null ? name : currentName[..^suffix.Length]);
        return SafeFiles.CreateUnique(directory, TestFileNames.Slug(source), suffix, YamlSerialization.Serializer.Serialize(document));
    }

    private sealed class NamedDocument
    {
        public string? Id { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>
    /// A new stable test id: 16 random hex characters, e.g. <c>3f9c2a7be41d06f5</c>.
    /// </summary>
    public static string NewTestId() => RandomNumberGenerator.GetHexString(16, lowercase: true);

    /// <summary>
    /// Copies a test, in the same folder, to a new file named after <paramref name="name"/>, as a starting point for a
    /// variation. The file is copied as it is: only its top-level <c>name:</c> changes and it gets a new <c>id:</c>, so
    /// comments, formatting and keys the editor does not know survive. Returns the copy's relative path. Throws
    /// <see cref="ArgumentException"/> when the test does not exist or the name is empty.
    /// </summary>
    public (string FilePath, string FileName) CloneTest(string folderPath, string fileName, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A name is required.", nameof(name));
        }

        var relative = TestPaths.Normalize(fileName);
        var sourcePath = CollectionPaths.TestFile(folderPath, relative);
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Test '{relative}' does not exist.", nameof(fileName));
        }

        var folder = TestPaths.FolderOf(relative);
        var content = WithTopLevel(WithTopLevel(File.ReadAllText(sourcePath), "name", name.Trim()), "id", NewTestId());
        var directory = Path.GetDirectoryName(sourcePath)!;
        var created = SafeFiles.CreateUnique(directory, TestFileNames.Slug(name), CollectionPaths.TestFileSuffix, content);
        return (Path.Combine(directory, created), TestPaths.Combine(folder, created));
    }

    /// <summary>
    /// Moves a test into <paramref name="targetFolder"/> (relative to the tests folder; empty for the top level) and
    /// returns its new relative path. The file itself is not rewritten, only moved; it keeps its file name unless that
    /// name is taken in the target folder, in which case the next free one is used. A folder left empty is removed.
    /// </summary>
    public string MoveTest(string folderPath, string fileName, string? targetFolder)
    {
        var relative = TestPaths.Normalize(fileName);
        var sourcePath = CollectionPaths.TestFile(folderPath, relative);
        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException($"Test '{relative}' does not exist.", nameof(fileName));
        }

        var folder = TestPaths.FolderForNewName(folderPath, targetFolder);
        if (string.Equals(folder, TestPaths.FolderOf(relative), StringComparison.Ordinal))
        {
            return relative;
        }

        var moved = SafeFiles.MoveUnique(
            sourcePath,
            TestPaths.FullPath(folderPath, folder, isFolder: true),
            CollectionPaths.ToTestId(TestPaths.FileNameOf(relative)),
            CollectionPaths.TestFileSuffix);
        SafeFiles.RemoveEmptyFolders(Path.GetDirectoryName(sourcePath)!, CollectionPaths.TestsDirectory(folderPath));
        return TestPaths.Combine(folder, moved);
    }

    /// <summary>
    /// Renames (or moves) a folder of tests and returns its new relative path. Refuses when a folder of that name
    /// already exists, rather than merging two folders' files; changing only letter case works.
    /// </summary>
    public string RenameFolder(string folderPath, string folder, string newFolder)
    {
        var from = TestPaths.NormalizeFolder(folder);
        var sourceDirectory = TestPaths.FullPath(folderPath, from, isFolder: true);
        if (from.Length == 0 || !Directory.Exists(sourceDirectory))
        {
            throw new ArgumentException($"Folder '{folder}' does not exist.", nameof(folder));
        }

        var to = TestPaths.FolderForNewName(folderPath, newFolder);
        if (to.Length == 0)
        {
            throw new ArgumentException("A folder name is required.", nameof(newFolder));
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return to;
        }

        var caseOnly = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && TestPaths.IsInFolder(to, from))
        {
            throw new ArgumentException("A folder cannot be moved into itself.", nameof(newFolder));
        }

        // Every test must still be within the nesting limit at its new place.
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, CollectionPaths.TestFilePattern, SearchOption.AllDirectories))
        {
            var inside = Path.GetRelativePath(sourceDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            TestPaths.Normalize($"{to}/{inside}");
        }

        var targetDirectory = TestPaths.FullPath(folderPath, to, isFolder: true);
        var targetParent = Path.GetDirectoryName(targetDirectory)!;
        if (caseOnly)
        {
            var temporary = Path.Combine(Path.GetDirectoryName(sourceDirectory)!, $".{Guid.NewGuid():N}.moving");
            Directory.Move(sourceDirectory, temporary);
            Directory.Move(temporary, targetDirectory);
            return to;
        }

        if (SafeFiles.ExistsIgnoringCase(targetParent, Path.GetFileName(targetDirectory)))
        {
            throw new ArgumentException($"A folder named '{to}' already exists. Move the tests into it one by one instead, so that no file is overwritten.", nameof(newFolder));
        }

        Directory.CreateDirectory(targetParent);
        Directory.Move(sourceDirectory, targetDirectory);          // throws rather than overwrite if it appeared meanwhile
        SafeFiles.RemoveEmptyFolders(Path.GetDirectoryName(sourceDirectory)!, CollectionPaths.TestsDirectory(folderPath));
        return to;
    }

    /// <summary>
    /// Deletes every test in a folder (and its subfolders) and returns how many were deleted. Other files in the
    /// folder are kept, and so are the folders that still hold them.
    /// </summary>
    public int DeleteFolder(string folderPath, string folder)
    {
        var from = TestPaths.NormalizeFolder(folder);
        if (from.Length == 0)
        {
            throw new ArgumentException("The top level of the tests folder cannot be deleted.", nameof(folder));
        }

        var directory = TestPaths.FullPath(folderPath, from, isFolder: true);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var tests = Directory.EnumerateFiles(directory, CollectionPaths.TestFilePattern, SearchOption.AllDirectories).ToList();
        foreach (var test in tests)
        {
            File.Delete(test);
        }

        foreach (var subfolder in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(subfolder).Any())
            {
                Directory.Delete(subfolder);
            }
        }

        SafeFiles.RemoveEmptyFolders(directory, CollectionPaths.TestsDirectory(folderPath));
        return tests.Count;
    }

    /// <summary>
    /// The YAML text with the top-level <paramref name="key"/> set to <paramref name="value"/> (added at the top when
    /// missing), leaving every other line untouched.
    /// </summary>
    private static string WithTopLevel(string yaml, string key, string value)
    {
        var line = $"{key}: " + YamlSerialization.Serializer.Serialize(value).TrimEnd();
        // The key with its value, including continuation lines of a multi-line value.
        var existing = Regex.Match(yaml, $@"^{Regex.Escape(key)}:[^\r\n]*(?:\r?\n[ \t]+[^\r\n]*)*", RegexOptions.Multiline);
        return existing.Success
            ? yaml[..existing.Index] + line + yaml[(existing.Index + existing.Length)..]
            : line + Environment.NewLine + yaml;
    }

    /// <summary>
    /// Deletes a test file; does nothing when it does not exist. A folder left empty is removed.
    /// </summary>
    public void DeleteTest(string folderPath, string fileName)
    {
        var path = CollectionPaths.TestFile(folderPath, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            SafeFiles.RemoveEmptyFolders(Path.GetDirectoryName(path)!, CollectionPaths.TestsDirectory(folderPath));
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
                Strict = assertion.Strict == true ? true : null,
                CaseSensitive = assertion.CaseSensitive == true ? true : null,
                Tolerance = assertion.Tolerance,
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
