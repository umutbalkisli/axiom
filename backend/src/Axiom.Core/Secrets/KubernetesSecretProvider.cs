using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace Axiom.Secrets;

/// <summary>
/// Reads Kubernetes Secrets through the API server using the pod's service account (in-cluster).
/// Outside a cluster set <c>AXIOM_K8S_API_URL</c> (and <c>AXIOM_K8S_TOKEN</c>), e.g. pointing at <c>kubectl proxy</c>.
/// <c>AXIOM_K8S_NAMESPACE</c> overrides the default namespace. The service account needs <c>get</c> on the secrets it reads.
/// </summary>
public sealed class KubernetesSecretProvider : ISecretProvider, IDisposable
{
    private readonly Lazy<HttpClient> _httpClient = new(CreateHttpClient);

    /// <summary>
    /// The provider name: <c>k8s</c>.
    /// </summary>
    public string Name => "k8s";

    /// <summary>
    /// The key names a data entry of a Kubernetes secret.
    /// </summary>
    public string KeyFormat => "secret-name/data-key  or  namespace/secret-name/data-key";

    /// <summary>
    /// Reads one data entry of a Kubernetes secret through the API server.
    /// </summary>
    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken)
    {
        var parts = key.Split('/', StringSplitOptions.TrimEntries);
        var (ns, name, dataKey) = parts.Length switch
        {
            2 => (DefaultNamespace(), parts[0], parts[1]),
            3 => (parts[0], parts[1], parts[2]),
            _ => throw new InvalidOperationException("Kubernetes key must look like 'secret-name/data-key' or 'namespace/secret-name/data-key'."),
        };

        var url = $"{ApiUrl()}/api/v1/namespaces/{Uri.EscapeDataString(ns)}/secrets/{Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = Token();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.Value.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Kubernetes API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var encoded = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?["data"]?[dataKey]?.GetValue<string>();
        return encoded is null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
    }

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    public void Dispose()
    {
        if (_httpClient.IsValueCreated)
        {
            _httpClient.Value.Dispose();
        }
    }

    private static string ServiceAccountDirectory() =>
        Environment.GetEnvironmentVariable("AXIOM_K8S_SERVICE_ACCOUNT_DIR") ?? "/var/run/secrets/kubernetes.io/serviceaccount";

    private static string ApiUrl()
    {
        var overrideUrl = Environment.GetEnvironmentVariable("AXIOM_K8S_API_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.TrimEnd('/');
        }

        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "443";
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Not running in a cluster and AXIOM_K8S_API_URL is not set.");
        }

        return $"https://{(host.Contains(':') ? $"[{host}]" : host)}:{port}";
    }

    private static string? Token()
    {
        var token = Environment.GetEnvironmentVariable("AXIOM_K8S_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        var tokenFile = Path.Combine(ServiceAccountDirectory(), "token");
        return File.Exists(tokenFile) ? File.ReadAllText(tokenFile).Trim() : null;
    }

    private static string DefaultNamespace()
    {
        var value = Environment.GetEnvironmentVariable("AXIOM_K8S_NAMESPACE");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var namespaceFile = Path.Combine(ServiceAccountDirectory(), "namespace");
        return File.Exists(namespaceFile) ? File.ReadAllText(namespaceFile).Trim() : "default";
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler();
        var caFile = Path.Combine(ServiceAccountDirectory(), "ca.crt");
        if (File.Exists(caFile))
        {
            var authority = X509CertificateLoader.LoadCertificateFromFile(caFile);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, _) =>
            {
                if (certificate is null || chain is null)
                {
                    return false;
                }

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(authority);
                return chain.Build(certificate);
            };
        }

        return new HttpClient(handler);
    }
}
