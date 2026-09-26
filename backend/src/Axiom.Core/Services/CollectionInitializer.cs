using Axiom.Documents;
using Axiom.Parsing;
using Axiom.Serialization;

namespace Axiom.Services;

/// <summary>
/// Creates a new collection folder with a starter collection file.
/// </summary>
public sealed class CollectionInitializer
{
    /// <summary>
    /// Creates the collection folder with a starter collection file. An existing collection file is left untouched.
    /// </summary>
    public async Task InitializeAsync(string folderPath, string collectionName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(CollectionPaths.TestsDirectory(folderPath));

        var collectionFile = CollectionPaths.CollectionFile(folderPath);
        if (File.Exists(collectionFile))
        {
            return;
        }

        var collection = new CollectionDocument
        {
            Name = collectionName,
            Description = "New API test collection",
            Variables = { ["base_url"] = "https://api.example.com" },
            RequestDefaults = { Headers = { ["Accept"] = "application/json" } },
        };

        await File.WriteAllTextAsync(collectionFile, YamlSerialization.Serializer.Serialize(collection), cancellationToken);
    }
}
