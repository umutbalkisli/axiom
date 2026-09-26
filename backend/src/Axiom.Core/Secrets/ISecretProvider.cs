namespace Axiom.Secrets;

/// <summary>
/// Reads secret values from one backing store (environment, Kubernetes, a vault, ...).
/// Register an implementation to add a store; collections reference it by <see cref="Name"/>.
/// Provider connection settings (addresses, tokens) come from the environment, never from collection files.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// The <c>provider</c> value used in a collection's <c>secrets</c> section (case-insensitive).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Describes the expected <c>key</c> format, shown as a hint in the UI.
    /// </summary>
    string KeyFormat { get; }

    /// <summary>
    /// Returns the secret value, or null when it does not exist. Throws when the store is misconfigured or unreachable.
    /// </summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken);
}
