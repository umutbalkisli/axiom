namespace Axiom.Parsing;

/// <summary>
/// A kind of file stored in its own folder of a collection, e.g. tests or shared step groups.
/// </summary>
public sealed record FileKind(string DirectoryName, string Suffix)
{
    /// <summary>
    /// Search pattern that matches every file of this kind.
    /// </summary>
    public string Pattern => "*" + Suffix;
}

/// <summary>
/// Knows the on-disk layout of a collection folder.
/// </summary>
public static class CollectionPaths
{
    /// <summary>
    /// Name of the collection file inside a collection folder.
    /// </summary>
    public const string CollectionFileName = "collection.yaml";
    /// <summary>
    /// Name of the folder that holds test files.
    /// </summary>
    public const string TestsDirectoryName = "tests";
    /// <summary>
    /// File name suffix of a test file.
    /// </summary>
    public const string TestFileSuffix = ".test.yaml";
    /// <summary>
    /// Search pattern that matches every test file.
    /// </summary>
    public const string TestFilePattern = "*" + TestFileSuffix;
    /// <summary>
    /// File name suffix of a shared-steps file.
    /// </summary>
    public const string SharedFileSuffix = ".shared.yaml";

    /// <summary>
    /// The kind of file that holds tests.
    /// </summary>
    public static readonly FileKind Tests = new(TestsDirectoryName, TestFileSuffix);
    /// <summary>
    /// The kind of file that holds shared step groups.
    /// </summary>
    public static readonly FileKind Shared = new("shared", SharedFileSuffix);

    /// <summary>
    /// The full path of the collection file in <paramref name="folderPath"/>.
    /// </summary>
    public static string CollectionFile(string folderPath) =>
        Path.Combine(Path.GetFullPath(folderPath), CollectionFileName);

    /// <summary>
    /// The full path of the folder that holds files of <paramref name="kind"/>.
    /// </summary>
    public static string Directory(string folderPath, FileKind kind) =>
        Path.Combine(Path.GetFullPath(folderPath), kind.DirectoryName);

    /// <summary>
    /// Resolves a file of the given kind inside the collection; rejects names that would escape its folder.
    /// </summary>
    public static string File(string folderPath, FileKind kind, string idOrFileName)
    {
        var name = ToFileName(kind, idOrFileName);
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not contain path separators.", nameof(idOrFileName));
        }

        return Path.Combine(Directory(folderPath, kind), name);
    }

    /// <summary>
    /// Returns the file name for an id, adding the suffix of <paramref name="kind"/> when it is missing.
    /// </summary>
    public static string ToFileName(FileKind kind, string idOrFileName) =>
        idOrFileName.EndsWith(kind.Suffix, StringComparison.OrdinalIgnoreCase)
            ? idOrFileName
            : idOrFileName + kind.Suffix;

    /// <summary>
    /// Returns the file name without the suffix of <paramref name="kind"/>.
    /// </summary>
    public static string ToId(FileKind kind, string fileName) =>
        fileName.EndsWith(kind.Suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^kind.Suffix.Length]
            : fileName;

    /// <summary>
    /// The full path of the tests folder.
    /// </summary>
    public static string TestsDirectory(string folderPath) => Directory(folderPath, Tests);

    /// <summary>
    /// Resolves a test file; rejects names that would escape the tests folder.
    /// </summary>
    public static string TestFile(string folderPath, string fileName) => File(folderPath, Tests, fileName);

    /// <summary>
    /// Returns the test file name for an id, adding the suffix when it is missing.
    /// </summary>
    public static string ToTestFileName(string idOrFileName) => ToFileName(Tests, idOrFileName);

    /// <summary>
    /// Returns the test file name without its suffix.
    /// </summary>
    public static string ToTestId(string fileName) => ToId(Tests, fileName);
}
