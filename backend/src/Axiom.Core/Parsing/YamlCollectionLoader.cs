using Axiom.Defaults;
using Axiom.Models;
using Microsoft.Data.Sqlite;
using Axiom.Serialization;
using Axiom.Validation;
using YamlDotNet.Core;

namespace Axiom.Parsing;

/// <summary>
/// Reads a collection folder (settings, tests and shared steps) into model objects and validates it.
/// </summary>
public sealed class YamlCollectionLoader
{
    /// <summary>
    /// Loads the collection in <paramref name="folderPath"/>; throws when it is missing or invalid.
    /// </summary>
    public LoadedCollection Load(string folderPath) => Load(folderPath, testFilter: null);

    /// <summary>
    /// Loads the collection in <paramref name="folderPath"/>, with only the test files whose path relative to the tests
    /// folder (e.g. <c>orders/get-user.test.yaml</c>) passes <paramref name="testFilter"/>; the others are not even parsed, so a broken
    /// file elsewhere does not stop the tests that were picked. Throws when the collection is missing or invalid.
    /// </summary>
    public LoadedCollection Load(string folderPath, Func<string, bool>? testFilter)
    {
        var root = Path.GetFullPath(folderPath);
        var collectionPath = CollectionPaths.CollectionFile(root);
        if (!File.Exists(collectionPath))
        {
            throw new FileNotFoundException($"{CollectionPaths.CollectionFileName} not found in folder", collectionPath);
        }

        var collection = DeserializeFile<CollectionDefinition>(collectionPath);
        NormalizeCollection(collection, root);
        EnsureValidVariableNames(collection.Variables.Keys, collectionPath);

        var shared = LoadShared(root);
        var tests = LoadTests(root, testFilter);
        EnsureIncludesExist(tests, shared);
        EnsureUniqueIds(tests);

        return new LoadedCollection
        {
            Collection = collection,
            TestCases = tests,
            SharedSteps = shared,
            RootPath = root,
        };
    }

    /// <summary>
    /// Parses a test that is not saved yet (for example one being edited), exactly as if it were loaded from a file
    /// called <paramref name="sourceName"/>.
    /// </summary>
    public TestCaseDefinition ParseTest(string yaml, string sourceName)
    {
        var test = Deserialize<TestCaseDefinition>(yaml, sourceName);
        NormalizeTest(test, sourceName);
        test.FileName = sourceName;
        EnsureValidVariableNames(test.Variables.Keys.Concat(SavedNames(test.Steps)), sourceName);
        return test;
    }

    private List<TestCaseDefinition> LoadTests(string root, Func<string, bool>? testFilter)
    {
        var testsPath = CollectionPaths.TestsDirectory(root);
        if (!Directory.Exists(testsPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(testsPath, CollectionPaths.TestFilePattern, SearchOption.AllDirectories)
                        .Select(path => (Path: path, Relative: TestPaths.RelativePath(root, path)))
                        .Where(file => testFilter is null || testFilter(file.Relative))
                        .OrderBy(file => file.Relative, StringComparer.OrdinalIgnoreCase)
                        .Select(file =>
                        {
                            var path = file.Path;
                            var test = DeserializeFile<TestCaseDefinition>(path);
                            NormalizeTest(test, path);
                            test.FileName = file.Relative;
                            EnsureValidVariableNames(
                                test.Variables.Keys.Concat(SavedNames(test.Steps)),
                                path);
                            return test;
                        })
                        .ToList();
    }

    private Dictionary<string, SharedStepsDefinition> LoadShared(string root)
    {
        var shared = new Dictionary<string, SharedStepsDefinition>(StringComparer.OrdinalIgnoreCase);
        var sharedPath = CollectionPaths.Directory(root, CollectionPaths.Shared);
        if (!Directory.Exists(sharedPath))
        {
            return shared;
        }

        foreach (var path in Directory.EnumerateFiles(sharedPath, CollectionPaths.Shared.Pattern, SearchOption.TopDirectoryOnly)
                                      .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var definition = DeserializeFile<SharedStepsDefinition>(path);
            definition.Steps ??= [];
            definition.SourceFile = path;
            NormalizeSteps(definition.Steps);
            EnsureValidVariableNames(SavedNames(definition.Steps), path);
            shared[CollectionPaths.ToId(CollectionPaths.Shared, Path.GetFileName(path))] = definition;
        }

        return shared;
    }

    private static IEnumerable<string> SavedNames(IEnumerable<StepDefinition> steps) =>
        steps.Select(s => s.SaveAs).OfType<string>().Where(n => n.Length > 0);

    private static void EnsureIncludesExist(IEnumerable<TestCaseDefinition> tests, IReadOnlyDictionary<string, SharedStepsDefinition> shared)
    {
        var owners = tests.Select(t => (File: t.SourceFile, Steps: t.Steps))
            .Concat(shared.Values.Select(d => (File: d.SourceFile, Steps: d.Steps)));
        foreach (var (file, steps) in owners)
        {
            foreach (var include in steps.Where(s => string.Equals(s.Type, "include", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(include.Ref) || !shared.ContainsKey(include.Ref.Trim()))
                {
                    throw new InvalidOperationException($"{file}: include step '{include.Id}' refers to shared steps '{include.Ref}', which do not exist.");
                }
            }
        }
    }

    /// <summary>
    /// A test's <c>id:</c> is its identity, so two files may not share one (typically a file copied by hand).
    /// </summary>
    private static void EnsureUniqueIds(IEnumerable<TestCaseDefinition> tests)
    {
        var duplicate = tests
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .GroupBy(t => t.Id!.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tests {string.Join(" and ", duplicate.Select(t => t.FileName))} have the same id '{duplicate.Key}'. "
                + "An id identifies one test: remove the id line from the copy (a new one is given when it is saved), or use Clone to copy a test.");
        }
    }

    private static void EnsureValidVariableNames(IEnumerable<string> names, string filePath)
    {
        foreach (var name in names)
        {
            if (VariableName.Check(name) is { } problem)
            {
                throw new InvalidOperationException($"{filePath}: {problem}");
            }
        }
    }

    private static T DeserializeFile<T>(string path) => Deserialize<T>(File.ReadAllText(path), path);

    /// <summary>
    /// Strict: a key the model does not know is an error that names the file, line and likely intended key.
    /// </summary>
    private static T Deserialize<T>(string yaml, string sourceName)
    {
        try
        {
            return YamlSerialization.StrictDeserializer.Deserialize<T>(yaml) ?? throw new InvalidOperationException($"{sourceName}: the file is empty.");
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException(YamlErrors.Describe(sourceName, ex), ex);
        }
    }

    private static void NormalizeCollection(CollectionDefinition collection, string rootPath)
    {
        collection.Connections ??= new(StringComparer.OrdinalIgnoreCase);
        collection.Variables ??= new(StringComparer.OrdinalIgnoreCase);
        collection.Secrets ??= new(StringComparer.OrdinalIgnoreCase);
        collection.RunSettings ??= new();
        collection.RequestDefaults ??= new();
        collection.RequestDefaults.Headers ??= new(StringComparer.OrdinalIgnoreCase);

        if (collection.RunSettings.MaxParallelTestCases <= 0)
        {
            collection.RunSettings.MaxParallelTestCases = RunSettingsDefaults.MaxParallelTestCases;
        }

        if (collection.RunSettings.StepTimeoutSeconds <= 0)
        {
            collection.RunSettings.StepTimeoutSeconds = RunSettingsDefaults.StepTimeoutSeconds;
        }

        foreach (var connection in collection.Connections.Values)
        {
            if (!string.Equals(connection.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(connection.ConnectionString))
            {
                continue;
            }

            var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                continue;
            }

            // A templated path (e.g. from a secret) is only known at run time; leave it untouched.
            if (builder.DataSource.Contains("{{", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Path.IsPathRooted(builder.DataSource))
            {
                builder.DataSource = Path.GetFullPath(Path.Combine(rootPath, builder.DataSource));
                connection.ConnectionString = builder.ToString();
            }
        }
    }

    private static void NormalizeTest(TestCaseDefinition test, string sourcePath)
    {
        test.Variables ??= new(StringComparer.OrdinalIgnoreCase);
        test.Steps ??= [];
        test.SourceFile = sourcePath;
        NormalizeSteps(test.Steps);
    }

    private static void NormalizeSteps(List<StepDefinition> steps)
    {
        foreach (var step in steps)
        {
            step.Headers ??= new(StringComparer.OrdinalIgnoreCase);
            step.QueryParams ??= new(StringComparer.OrdinalIgnoreCase);
            step.Assert ??= [];
            step.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id;
            step.Type = step.Type.Trim();
            step.Name = string.IsNullOrWhiteSpace(step.Name) ? step.Id : step.Name;
        }
    }
}