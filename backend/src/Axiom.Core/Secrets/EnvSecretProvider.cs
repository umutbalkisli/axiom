namespace Axiom.Secrets;

/// <summary>Reads secrets from environment variables. The natural choice for CI/CD pipelines.</summary>
public sealed class EnvSecretProvider : ISecretProvider
{
    public string Name => "env";

    public string KeyFormat => "ENV_VAR_NAME";

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Environment.GetEnvironmentVariable(key));
}
