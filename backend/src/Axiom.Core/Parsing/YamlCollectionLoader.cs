using Axiom.Defaults;
using Axiom.Models;
using Microsoft.Data.Sqlite;
using Axiom.Serialization;

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

        var testsPath = CollectionPaths.TestsDirectory(root);
        if (!Directory.Exists(testsPath))
        {
            return new LoadedCollection
            {
                Collection = collection,
                TestCases = [],
                RootPath = root,
            };
        }

        var tests = Directory.EnumerateFiles(testsPath, CollectionPaths.TestFilePattern, SearchOption.AllDirectories)
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                             .Select(path =>
                             {
                                var test = DeserializeFile<TestCaseDefinition>(path);
                                NormalizeTest(test, path);
                                return test;
                             })
                             .ToList();

        return new LoadedCollection
        {
            Collection = collection,
            TestCases = tests,
            RootPath = root,
        };
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

        foreach (var step in test.Steps)
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