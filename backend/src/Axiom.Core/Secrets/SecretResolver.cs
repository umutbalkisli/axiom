using Axiom.Models;

namespace Axiom.Secrets;

public sealed class SecretResolver
{
    private readonly Dictionary<string, ISecretProvider> _providers;

    public SecretResolver(IEnumerable<ISecretProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ISecretProvider> Providers => _providers.Values;

    /// <summary>
    /// Reads every declared secret up front so a missing one fails the run before any step executes.
    /// <paramref name="environment"/> selects each secret's environment-specific source when it has one.
    /// </summary>
    public async Task<RunSecrets> ResolveAsync(IReadOnlyDictionary<string, SecretReference> declared, string? environment, CancellationToken cancellationToken)
    {
        if (declared.Count == 0)
        {
            return RunSecrets.Empty;
        }

        var lookups = await Task.WhenAll(declared.Select(pair => ResolveOneAsync(pair.Key, pair.Value.SourceFor(environment), cancellationToken)));

        var failures = lookups.Where(l => l.Error is not null).Select(l => $"{l.Name}: {l.Error}").ToList();
        if (failures.Count > 0)
        {
            throw new SecretResolutionException("Could not resolve secrets:" + Environment.NewLine + string.Join(Environment.NewLine, failures.Select(f => "  - " + f)));
        }

        return new RunSecrets(lookups.ToDictionary(l => l.Name, l => l.Value!));
    }

    private async Task<(string Name, string? Value, string? Error)> ResolveOneAsync(string name, SecretSource reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference.Provider) || string.IsNullOrWhiteSpace(reference.Key))
        {
            return (name, null, "provider and key are required");
        }

        if (!_providers.TryGetValue(reference.Provider.Trim(), out var provider))
        {
            return (name, null, $"unknown provider '{reference.Provider}' (available: {string.Join(", ", _providers.Keys)})");
        }

        try
        {
            var value = await provider.GetSecretAsync(reference.Key.Trim(), cancellationToken);
            return string.IsNullOrEmpty(value)
                ? (name, null, $"not found (or empty) in provider '{provider.Name}'")
                : (name, value, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (name, null, $"provider '{provider.Name}' failed: {ex.Message}");
        }
    }
}
