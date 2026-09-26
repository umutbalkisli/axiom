using Axiom.Defaults;
using Axiom.Models;
using Microsoft.Data.Sqlite;
using Axiom.Serialization;
using Axiom.Validation;

namespace Axiom.Parsing;

public sealed class YamlCollectionLoader
{
    public LoadedCollection Load(string folderPath)
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
        var tests = LoadTests(root);
        EnsureIncludesExist(tests, shared);

        return new LoadedCollection
        {
            Collection = collection,
            TestCases = tests,
            SharedSteps = shared,
            RootPath = root,
        };
    }

    private List<TestCaseDefinition> LoadTests(string root)
    {
        var testsPath = CollectionPaths.TestsDirectory(root);
        if (!Directory.Exists(testsPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(testsPath, CollectionPaths.TestFilePattern, SearchOption.AllDirectories)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(path =>
                        {
                            var test = DeserializeFile<TestCaseDefinition>(path);
                            NormalizeTest(test, path);
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

    private T DeserializeFile<T>(string path)
    {
        var yaml = File.ReadAllText(path);
        return YamlSerialization.Deserializer.Deserialize<T>(yaml) ?? throw new InvalidOperationException($"YAML could not be parsed: {path}");
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