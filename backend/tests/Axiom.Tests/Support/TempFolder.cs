namespace Axiom.Tests.Support;

/// <summary>
/// A scratch directory that is deleted when the test is done.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axiom-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>
    /// Writes a file (creating folders as needed) and returns its full path.
    /// </summary>
    public string Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public bool Exists(string relativePath) => File.Exists(System.IO.Path.Combine(Path, relativePath));

    public string Read(string relativePath) => File.ReadAllText(System.IO.Path.Combine(Path, relativePath));

    public string[] Files(string relativeFolder) =>
        Directory.Exists(System.IO.Path.Combine(Path, relativeFolder))
            ? Directory.GetFiles(System.IO.Path.Combine(Path, relativeFolder)).Select(file => System.IO.Path.GetFileName(file)).Order().ToArray()
            : [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp folder must not fail a test
        }
    }
}
