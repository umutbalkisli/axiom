namespace Axiom.Models;

/// <summary>Where one secret value is read from: a provider and that provider's key.</summary>
public class SecretSource
{
    public string Provider { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Points at a secret held elsewhere. The collection file stores only this reference, never the value.
/// The provider and key are the default source; <see cref="Environments"/> replaces the source for a named
/// environment (for example a local vault on a laptop, environment variables in CI, a Kubernetes secret in a cluster).
/// </summary>
public sealed class SecretReference : SecretSource
{
    public Dictionary<string, SecretSource>? Environments { get; set; }

    /// <summary>The source to use for <paramref name="environment"/>, or the default when it has no override.</summary>
    public SecretSource SourceFor(string? environment)
    {
        if (!string.IsNullOrWhiteSpace(environment) && Environments is not null)
        {
            foreach (var (name, source) in Environments)
            {
                if (string.Equals(name, environment.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }
        }

        return this;
    }
}
