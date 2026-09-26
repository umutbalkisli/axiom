namespace Axiom.Parsing;

/// <summary>A kind of file stored in its own folder of a collection, e.g. tests or shared step groups.</summary>
public sealed record FileKind(string DirectoryName, string Suffix)
{
    public string Pattern => "*" + Suffix;
}

/// <summary>
/// Knows the on-disk layout of a collection folder.
/// </summary>
public static class CollectionPaths
{
    public const string CollectionFileName = "collection.yaml";
    public const string TestsDirectoryName = "tests";
    public const string TestFileSuffix = ".test.yaml";
    public const string TestFilePattern = "*" + TestFileSuffix;
    public const string SharedFileSuffix = ".shared.yaml";

    public static readonly FileKind Tests = new(TestsDirectoryName, TestFileSuffix);
    public static readonly FileKind Shared = new("shared", SharedFileSuffix);

    public static string CollectionFile(string folderPath) =>
        Path.Combine(Path.GetFullPath(folderPath), CollectionFileName);

    public static string Directory(string folderPath, FileKind kind) =>
        Path.Combine(Path.GetFullPath(folderPath), kind.DirectoryName);

    /// <summary>Resolves a file of the given kind inside the collection; rejects names that would escape its folder.</summary>
    public static string File(string folderPath, FileKind kind, string idOrFileName)
    {
        var name = ToFileName(kind, idOrFileName);
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not contain path separators.", nameof(idOrFileName));
        }

        return Path.Combine(Directory(folderPath, kind), name);
    }

    public static string ToFileName(FileKind kind, string idOrFileName) =>
        idOrFileName.EndsWith(kind.Suffix, StringComparison.OrdinalIgnoreCase)
            ? idOrFileName
            : idOrFileName + kind.Suffix;

    public static string ToId(FileKind kind, string fileName) =>
        fileName.EndsWith(kind.Suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^kind.Suffix.Length]
            : fileName;

    public static string TestsDirectory(string folderPath) => Directory(folderPath, Tests);

    public static string TestFile(string folderPath, string fileName) => File(folderPath, Tests, fileName);

    public static string ToTestFileName(string idOrFileName) => ToFileName(Tests, idOrFileName);

    public static string ToTestId(string fileName) => ToId(Tests, fileName);
}
