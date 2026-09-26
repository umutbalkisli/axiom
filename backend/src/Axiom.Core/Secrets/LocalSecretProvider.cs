namespace Axiom.Secrets;

/// <summary>
/// Serves values handed in by the caller for the current run (the desktop app passes secrets it keeps
/// in the operating system's secure storage). Nothing is persisted by the engine.
/// </summary>
public sealed class LocalSecretProvider : ISecretProvider
{
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();

    public string Name => "local";

    public string KeyFormat => "name";

    public void Load(IReadOnlyDictionary<string, string>? values) =>
        _values = values ?? new Dictionary<string, string>();

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
}
