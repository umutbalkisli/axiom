using System.Text.Json.Nodes;
using Axiom.Network;

namespace Axiom.Secrets;

/// <summary>
/// HashiCorp Vault KV secrets engine. Configure with <c>VAULT_ADDR</c>, <c>VAULT_TOKEN</c>,
/// optional <c>VAULT_NAMESPACE</c> and <c>AXIOM_VAULT_KV_VERSION</c> (1 or 2, default 2).
/// </summary>
public sealed class VaultSecretProvider : ISecretProvider, IDisposable
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates the provider; it reaches Vault like every other call, through <paramref name="network"/>.
    /// </summary>
    public VaultSecretProvider(NetworkSettings? network = null)
    {
        _httpClient = new HttpClient(AxiomHttp.CreateHandler(network ?? NetworkSettings.FromEnvironment()));
    }

    /// <summary>
    /// The provider name: <c>vault</c>.
    /// </summary>
    public string Name => "vault";

    /// <summary>
    /// The key looks like <c>mount/path#field</c>.
    /// </summary>
    public string KeyFormat => "mount/path#field";

    /// <summary>
    /// Reads one field of a secret from HashiCorp Vault.
    /// </summary>
    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken)
    {
        var address = Environment.GetEnvironmentVariable("VAULT_ADDR");
        var token = Environment.GetEnvironmentVariable("VAULT_TOKEN");
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("VAULT_ADDR and VAULT_TOKEN must be set.");
        }

        var (secretPath, field) = SplitKey(key);
        var slash = secretPath.IndexOf('/');
        if (slash <= 0 || slash == secretPath.Length - 1)
        {
            throw new InvalidOperationException("Vault key must look like 'mount/path#field'.");
        }

        var mount = secretPath[..slash];
        var path = secretPath[(slash + 1)..];
        var kv1 = Environment.GetEnvironmentVariable("AXIOM_VAULT_KV_VERSION") == "1";
        var url = $"{address.TrimEnd('/')}/v1/{mount}/{(kv1 ? string.Empty : "data/")}{path}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Vault-Token", token);
        var vaultNamespace = Environment.GetEnvironmentVariable("VAULT_NAMESPACE");
        if (!string.IsNullOrWhiteSpace(vaultNamespace))
        {
            request.Headers.Add("X-Vault-Namespace", vaultNamespace);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vault returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var data = kv1 ? body?["data"] : body?["data"]?["data"];
        return data?[field]?.ToString();
    }

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    public void Dispose() => _httpClient.Dispose();

    private static (string Path, string Field) SplitKey(string key)
    {
        var hash = key.LastIndexOf('#');
        if (hash <= 0 || hash == key.Length - 1)
        {
            throw new InvalidOperationException("Vault key must look like 'mount/path#field'.");
        }

        return (key[..hash], key[(hash + 1)..]);
    }
}
