namespace Axiom.Parsing;

/// <summary>
/// Paths of test files relative to a collection's <c>tests</c> folder, such as <c>orders/create-order.test.yaml</c>.
/// That relative path is how a test is found everywhere (listing, running, results); it always uses <c>/</c>, so it
/// reads the same on every system. Nothing built here can point outside the <c>tests</c> folder.
/// </summary>
public static class TestPaths
{
    /// <summary>
    /// How deep folders inside <c>tests</c> may be nested.
    /// </summary>
    public const int MaxFolderDepth = 3;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>
    /// A test's relative path in canonical form (<c>/</c> separators, <c>.test.yaml</c> suffix): <c>orders\create</c>
    /// becomes <c>orders/create.test.yaml</c>. Throws <see cref="ArgumentException"/> for anything that is not a
    /// safe path inside the tests folder.
    /// </summary>
    public static string Normalize(string relativePath)
    {
        var segments = Split(relativePath, nameof(relativePath));
        if (segments.Count == 0)
        {
            throw new ArgumentException("A test file name is required.", nameof(relativePath));
        }

        segments[^1] = CollectionPaths.ToTestFileName(segments[^1]);
        CheckDepth(segments.Count - 1, nameof(relativePath));
        return string.Join('/', segments);
    }

    /// <summary>
    /// A folder inside <c>tests</c> in canonical form; empty for the top level. Throws <see cref="ArgumentException"/>
    /// for anything unsafe or nested deeper than <see cref="MaxFolderDepth"/>.
    /// </summary>
    public static string NormalizeFolder(string? folder)
    {
        var segments = Split(folder ?? string.Empty, nameof(folder));
        CheckDepth(segments.Count, nameof(folder));
        return string.Join('/', segments);
    }

    /// <summary>
    /// The folder a new name should create: segments that already exist on disk keep their spelling, new ones follow
    /// the file naming rules (<c>Orders API</c> becomes <c>orders-api</c>).
    /// </summary>
    public static string FolderForNewName(string collectionFolder, string? folder)
    {
        var segments = Split(folder ?? string.Empty, nameof(folder));
        CheckDepth(segments.Count, nameof(folder));
        var current = CollectionPaths.TestsDirectory(collectionFolder);
        var result = new List<string>(segments.Count);
        foreach (var segment in segments)
        {
            var existing = Directory.Exists(current)
                ? Directory.EnumerateDirectories(current).Select(Path.GetFileName).FirstOrDefault(name => string.Equals(name, segment, StringComparison.Ordinal))
                : null;
            var name = existing ?? TestFileNames.Slug(segment);
            if (ReservedNames.Contains(name))
            {
                throw new ArgumentException($"'{name}' cannot be used as a folder name.", nameof(folder));
            }

            result.Add(name);
            current = Path.Combine(current, name);
        }

        return string.Join('/', result);
    }

    /// <summary>
    /// The folder part of a relative test path; empty at the top level.
    /// </summary>
    public static string FolderOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? string.Empty : relativePath[..slash];
    }

    /// <summary>
    /// The file name part of a relative test path.
    /// </summary>
    public static string FileNameOf(string relativePath) => relativePath[(relativePath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// <paramref name="fileName"/> inside <paramref name="folder"/>.
    /// </summary>
    public static string Combine(string folder, string fileName) =>
        string.IsNullOrEmpty(folder) ? fileName : $"{folder}/{fileName}";

    /// <summary>
    /// True when <paramref name="relativePath"/> is <paramref name="folder"/> itself or inside it.
    /// </summary>
    public static bool IsInFolder(string relativePath, string folder) =>
        folder.Length == 0
        || relativePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(relativePath, folder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The full path of a test (or, with <paramref name="isFolder"/>, a folder) inside the collection's tests folder.
    /// </summary>
    public static string FullPath(string collectionFolder, string relativePath, bool isFolder = false)
    {
        var testsDirectory = CollectionPaths.TestsDirectory(collectionFolder);
        var normalized = isFolder ? NormalizeFolder(relativePath) : Normalize(relativePath);
        var full = Path.GetFullPath(Path.Combine(testsDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));

        // Belt and braces: Split already refuses "..", but the result must be inside the tests folder whatever happens.
        if (!full.StartsWith(testsDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != testsDirectory)
        {
            throw new ArgumentException("The path must stay inside the tests folder.", nameof(relativePath));
        }

        return full;
    }

    /// <summary>
    /// The relative path of a file (or folder) inside the collection's tests folder.
    /// </summary>
    public static string RelativePath(string collectionFolder, string fullPath) =>
        Path.GetRelativePath(CollectionPaths.TestsDirectory(collectionFolder), fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private static List<string> Split(string path, string parameterName)
    {
        if (Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be relative to the tests folder.", parameterName);
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var segment in segments)
        {
            if (segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.Contains(':')
                || segment.EndsWith('.')
                // Windows reserves these names with any extension too: con.test.yaml is as unusable as con.
                || ReservedNames.Contains(segment.Split('.')[0]))
            {
                throw new ArgumentException($"'{segment}' cannot be used in a test path.", parameterName);
            }
        }

        return segments;
    }

    private static void CheckDepth(int depth, string parameterName)
    {
        if (depth > MaxFolderDepth)
        {
            throw new ArgumentException($"Folders can be nested at most {MaxFolderDepth} deep.", parameterName);
        }
    }
}
