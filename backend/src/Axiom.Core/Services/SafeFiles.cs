namespace Axiom.Services;

/// <summary>
/// File operations that can never overwrite another file. Instead of "check, then write" (which a file appearing in
/// between would defeat), the file system itself refuses: new files are created with <see cref="FileMode.CreateNew"/>
/// and moves use <c>overwrite: false</c>. When a name is taken, the next free one (<c>name-2</c>, <c>name-3</c>, ...) is used.
/// </summary>
internal static class SafeFiles
{
    /// <summary>
    /// Creates a file named <paramref name="slug"/> (or the next free variant) plus <paramref name="suffix"/> in
    /// <paramref name="directory"/> and returns its file name.
    /// </summary>
    public static string CreateUnique(string directory, string slug, string suffix, string content)
    {
        Directory.CreateDirectory(directory);
        foreach (var candidate in Candidates(slug, suffix))
        {
            if (ExistsIgnoringCase(directory, candidate))
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(Path.Combine(directory, candidate), FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return candidate;
            }
            catch (IOException) when (File.Exists(Path.Combine(directory, candidate)))
            {
                // Taken since we looked: try the next name.
            }
        }

        throw new InvalidOperationException("Unreachable: the candidate names never run out.");
    }

    /// <summary>
    /// Moves <paramref name="sourcePath"/> into <paramref name="directory"/> as <paramref name="slug"/> (or the next
    /// free variant) plus <paramref name="suffix"/> and returns the new file name. Moving a file onto its own name does
    /// nothing; a change of letter case only is done through a temporary name, so it also works on file systems that
    /// ignore case.
    /// </summary>
    public static string MoveUnique(string sourcePath, string directory, string slug, string suffix)
    {
        Directory.CreateDirectory(directory);
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var sourceName = Path.GetFileName(sourcePath);
        var sameDirectory = string.Equals(sourceDirectory, Path.GetFullPath(directory), StringComparison.Ordinal);

        foreach (var candidate in Candidates(slug, suffix))
        {
            var target = Path.Combine(directory, candidate);
            if (sameDirectory && string.Equals(sourceName, candidate, StringComparison.Ordinal))
            {
                return candidate;
            }

            if (sameDirectory && string.Equals(sourceName, candidate, StringComparison.OrdinalIgnoreCase))
            {
                // The same file with other letter case (Get.test.yaml -> get.test.yaml).
                var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.moving");
                File.Move(sourcePath, temporary, overwrite: false);
                File.Move(temporary, target, overwrite: false);
                return candidate;
            }

            if (ExistsIgnoringCase(directory, candidate))
            {
                continue;
            }

            try
            {
                File.Move(sourcePath, target, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(target))
            {
                // Taken since we looked: try the next name.
            }
        }

        throw new InvalidOperationException("Unreachable: the candidate names never run out.");
    }

    /// <summary>
    /// Removes <paramref name="directory"/> and then its parents while they are completely empty, stopping at
    /// <paramref name="stopAt"/> (which is never removed). A folder holding anything at all is left alone.
    /// </summary>
    public static void RemoveEmptyFolders(string directory, string stopAt)
    {
        var current = Path.GetFullPath(directory);
        var root = Path.GetFullPath(stopAt);
        while (current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current)!;
        }
    }

    /// <summary>
    /// True when a file or folder named <paramref name="name"/> exists in <paramref name="directory"/>, whatever its
    /// letter case: on a case-insensitive disk those are the same file, and on a case-sensitive one two files differing
    /// only in case would be confusing.
    /// </summary>
    public static bool ExistsIgnoringCase(string directory, string name) =>
        Directory.Exists(directory)
        && Directory.EnumerateFileSystemEntries(directory)
            .Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Candidates(string slug, string suffix)
    {
        yield return slug + suffix;
        for (var number = 2; ; number++)
        {
            yield return $"{slug}-{number}{suffix}";
        }
    }
}
