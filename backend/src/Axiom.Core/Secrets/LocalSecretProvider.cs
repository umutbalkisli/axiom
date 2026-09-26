namespace Axiom.Secrets;

/// <summary>
/// Serves values handed in by the caller for the current run (the desktop app passes secrets it keeps
/// in the operating system's secure storage). Nothing is persisted by the engine.
/// </summary>
public sealed class LocalSecretProvider : ISecretProvider
{
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();

    /// <summary>
    /// The provider name: <c>local</c>.
    /// </summary>
    public string Name => "local";

    /// <summary>
    /// The key is the name the value was handed in under.
    /// </summary>
    public string KeyFormat => "name";

    /// <summary>
    /// Sets the values available for the current run.
    /// </summary>
    public void Load(IReadOnlyDictionary<string, string>? values) =>
        _values = values ?? new Dictionary<string, string>();

    /// <summary>
    /// Returns the value handed in under <paramref name="key"/>, or null.
    /// </summary>
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
}
