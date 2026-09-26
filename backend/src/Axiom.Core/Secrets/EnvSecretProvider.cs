namespace Axiom.Secrets;

/// <summary>
/// Reads secrets from environment variables. The natural choice for CI/CD pipelines.
/// </summary>
public sealed class EnvSecretProvider : ISecretProvider
{
    /// <summary>
    /// The provider name: <c>env</c>.
    /// </summary>
    public string Name => "env";

    /// <summary>
    /// The key is the name of an environment variable.
    /// </summary>
    public string KeyFormat => "ENV_VAR_NAME";

    /// <summary>
    /// Reads the environment variable named <paramref name="key"/>.
    /// </summary>
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Environment.GetEnvironmentVariable(key));
}
