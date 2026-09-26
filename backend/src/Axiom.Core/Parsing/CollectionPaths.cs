namespace Axiom.Parsing;

/// <summary>
/// Knows the on-disk layout of a collection folder.
/// </summary>
public static class CollectionPaths
{
    public const string CollectionFileName = "collection.yaml";
    public const string TestsDirectoryName = "tests";
    public const string TestFileSuffix = ".test.yaml";
    public const string TestFilePattern = "*" + TestFileSuffix;

    public static string CollectionFile(string folderPath) =>
        Path.Combine(Path.GetFullPath(folderPath), CollectionFileName);

    public static string TestsDirectory(string folderPath) =>
        Path.Combine(Path.GetFullPath(folderPath), TestsDirectoryName);

    /// <summary>Resolves a test file inside the collection's tests directory; rejects names that would escape it.</summary>
    public static string TestFile(string folderPath, string fileName)
    {
        var name = ToTestFileName(fileName);
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new ArgumentException("Test file name must not contain path separators.", nameof(fileName));
        }

        return Path.Combine(TestsDirectory(folderPath), name);
    }

    public static string ToTestFileName(string idOrFileName) =>
        idOrFileName.EndsWith(TestFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? idOrFileName
            : idOrFileName + TestFileSuffix;

    public static string ToTestId(string fileName) =>
        fileName.EndsWith(TestFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^TestFileSuffix.Length]
            : fileName;
}
