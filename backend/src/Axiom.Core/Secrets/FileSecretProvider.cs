namespace Axiom.Secrets;

/// <summary>
/// Reads secrets from files under the directory named by <c>AXIOM_SECRETS_DIR</c>
/// (for example a Kubernetes secret volume or a Docker secrets mount). Keys cannot escape that directory.
/// </summary>
public sealed class FileSecretProvider : ISecretProvider
{
    /// <summary>
    /// The environment variable that names the secrets directory.
    /// </summary>
    public const string DirectoryVariable = "AXIOM_SECRETS_DIR";

    /// <summary>
    /// The provider name: <c>file</c>.
    /// </summary>
    public string Name => "file";

    /// <summary>
    /// The key is a relative file name inside the secrets directory.
    /// </summary>
    public string KeyFormat => "relative/file/name";

    /// <summary>
    /// Reads the file <paramref name="key"/> inside the secrets directory; the key cannot escape it.
    /// </summary>
    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken)
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"{DirectoryVariable} is not set.");
        }

        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Secret file must be inside the secrets directory.");
        }

        if (!File.Exists(path))
        {
            return null;
        }

        return (await File.ReadAllTextAsync(path, cancellationToken)).TrimEnd('\r', '\n');
    }
}
