using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Network;

/// <summary>
/// How Axiom reaches the APIs and services a collection talks to: through which proxy, signed in how, and trusting
/// which extra certificates. Read from the environment, so the same collection works on a laptop behind a company proxy
/// and on a build server:
/// <list type="bullet">
/// <item><c>AXIOM_PROXY</c>: <c>system</c> (the default: the system's proxy settings, including PAC scripts on Windows
/// and macOS, or <c>HTTPS_PROXY</c> / <c>HTTP_PROXY</c> / <c>NO_PROXY</c> when set), <c>none</c> (always direct), or a
/// proxy address such as <c>http://proxy.corp:8080</c> (with <c>NO_PROXY</c> for exceptions).</item>
/// <item>Proxy sign-in: the Windows sign-in (NTLM / Kerberos) is sent to a proxy that asks for it; a proxy that wants a
/// user name and password gets them from its address: <c>http://user:password@proxy.corp:8080</c>.</item>
/// <item><c>AXIOM_CA_CERTS</c>: a PEM file of extra root certificates to trust, for a company proxy that inspects HTTPS
/// with its own certificate.</item>
/// </list>
/// </summary>
public sealed class NetworkSettings
{
    /// <summary>
    /// The proxy to use; null means direct connections only.
    /// </summary>
    public IWebProxy? Proxy { get; }

    /// <summary>
    /// How the proxy was chosen, for messages: "system settings", "none", or the configured address.
    /// </summary>
    public string ProxySource { get; }

    /// <summary>
    /// Extra root certificates to trust besides the system's.
    /// </summary>
    public X509Certificate2Collection ExtraRoots { get; }

    /// <summary>
    /// Where <see cref="ExtraRoots"/> came from, for messages.
    /// </summary>
    public string? ExtraRootsFile { get; }

    /// <summary>
    /// Creates settings; <paramref name="proxy"/> is <c>system</c>, <c>none</c> or a proxy address. With <c>system</c>,
    /// <paramref name="httpsProxy"/> / <paramref name="httpProxy"/> (the <c>HTTPS_PROXY</c> / <c>HTTP_PROXY</c> /
    /// <c>ALL_PROXY</c> variables) win over the system's settings when given.
    /// </summary>
    public NetworkSettings(string? proxy = null, string? noProxy = null, string? caCertificatesFile = null, string? httpsProxy = null, string? httpProxy = null)
    {
        var setting = string.IsNullOrWhiteSpace(proxy) ? "system" : proxy.Trim();
        (Proxy, ProxySource) = setting.ToLowerInvariant() switch
        {
            "system" when !string.IsNullOrWhiteSpace(httpsProxy) || !string.IsNullOrWhiteSpace(httpProxy) =>
                ((IWebProxy?)SchemeProxy.Create(httpsProxy, httpProxy, noProxy), "HTTPS_PROXY / HTTP_PROXY"),
            "system" => (HttpClient.DefaultProxy, "system settings"),
            "none" or "direct" => (null, "none"),
            _ => (ExplicitProxy.Parse(setting, noProxy), ExplicitProxy.Redact(setting)),
        };

        ExtraRoots = [];
        if (!string.IsNullOrWhiteSpace(caCertificatesFile))
        {
            ExtraRootsFile = caCertificatesFile.Trim();
            if (!File.Exists(ExtraRootsFile))
            {
                throw new InvalidOperationException($"AXIOM_CA_CERTS points to '{ExtraRootsFile}', which does not exist.");
            }

            try
            {
                ExtraRoots.ImportFromPemFile(ExtraRootsFile);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                throw new InvalidOperationException($"AXIOM_CA_CERTS: '{ExtraRootsFile}' is not a PEM certificate file ({ex.Message}).", ex);
            }

            if (ExtraRoots.Count == 0)
            {
                throw new InvalidOperationException($"AXIOM_CA_CERTS: '{ExtraRootsFile}' contains no certificate.");
            }
        }
    }

    /// <summary>
    /// The settings in the environment variables described on <see cref="NetworkSettings"/>.
    /// </summary>
    public static NetworkSettings FromEnvironment() => new(
        Environment.GetEnvironmentVariable("AXIOM_PROXY"),
        Variable("NO_PROXY"),
        Environment.GetEnvironmentVariable("AXIOM_CA_CERTS"),
        Variable("HTTPS_PROXY") ?? Variable("ALL_PROXY"),
        Variable("HTTP_PROXY") ?? Variable("ALL_PROXY"));

    /// <summary>
    /// A proxy variable in either spelling (tools disagree on upper or lower case).
    /// </summary>
    private static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } upper ? upper
        : Environment.GetEnvironmentVariable(name.ToLowerInvariant()) is { Length: > 0 } lower ? lower
        : null;

    /// <summary>
    /// A proxy address as it may be shown: scheme, host and port, never a user name or password.
    /// </summary>
    public static string Display(Uri proxy) => $"{proxy.Scheme}://{proxy.Host}:{proxy.Port}";

    /// <summary>
    /// The proxy a request to <paramref name="uri"/> goes through; null when it goes direct.
    /// </summary>
    public Uri? ProxyFor(Uri uri)
    {
        try
        {
            return Proxy is null || Proxy.IsBypassed(uri) ? null : Proxy.GetProxy(uri);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;   // a failing PAC script: the request itself will report the problem
        }
    }
}

/// <summary>
/// Proxy addresses as they may be shown.
/// </summary>
public static class ExplicitProxyAddress
{
    /// <summary>
    /// The address with its password replaced by <c>***</c>.
    /// </summary>
    public static string Redact(string value) => ExplicitProxy.Redact(value);
}

/// <summary>
/// <c>HTTPS_PROXY</c> for https addresses and <c>HTTP_PROXY</c> for http ones, with the same <c>NO_PROXY</c> rules as
/// <c>AXIOM_PROXY</c> (<c>corp.com</c> covers its subdomains, as in curl).
/// </summary>
internal sealed class SchemeProxy(ExplicitProxy? https, ExplicitProxy? http) : IWebProxy
{
    public static SchemeProxy Create(string? httpsProxy, string? httpProxy, string? noProxy) => new(
        string.IsNullOrWhiteSpace(httpsProxy) ? null : ExplicitProxy.Parse(httpsProxy, noProxy),
        string.IsNullOrWhiteSpace(httpProxy) ? null : ExplicitProxy.Parse(httpProxy, noProxy));

    public ICredentials? Credentials { get; set; }

    private ExplicitProxy? For(Uri destination) => destination.Scheme == Uri.UriSchemeHttps ? https : http;

    public Uri? GetProxy(Uri destination) => For(destination)?.GetProxy(destination);

    public bool IsBypassed(Uri host) => For(host)?.IsBypassed(host) ?? true;

    /// <summary>
    /// The sign-in for the proxy that <paramref name="destination"/> goes through.
    /// </summary>
    public ICredentials? CredentialsFor(Uri destination) => For(destination)?.Credentials;
}

/// <summary>
/// A proxy given by address, with <c>NO_PROXY</c> exceptions and sign-in from the address or the current user.
/// </summary>
internal sealed class ExplicitProxy(Uri address, NoProxyList bypass, ICredentials credentials) : IWebProxy
{
    public ICredentials? Credentials { get; set; } = credentials;

    public static ExplicitProxy Parse(string value, string? noProxy)
    {
        var text = value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"AXIOM_PROXY must be 'system', 'none' or a proxy address such as http://proxy:8080 (got '{Redact(value)}').");
        }

        ICredentials credentials = CredentialCache.DefaultCredentials;
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            var parts = parsed.UserInfo.Split(':', 2);
            credentials = new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        var address = new UriBuilder(parsed) { UserName = string.Empty, Password = string.Empty }.Uri;
        return new ExplicitProxy(address, NoProxyList.Parse(noProxy), credentials);
    }

    /// <summary>
    /// The address without a password, for messages.
    /// </summary>
    public static string Redact(string value) =>
        Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { Password = uri.UserInfo.Contains(':') ? "***" : string.Empty }.Uri.ToString()
            : value;

    public Uri? GetProxy(Uri destination) => IsBypassed(destination) ? null : address;

    public bool IsBypassed(Uri host) => host.IsLoopback || bypass.Matches(host.Host);
}

/// <summary>
/// The hosts that go direct, in the usual <c>NO_PROXY</c> format: comma-separated names, where <c>corp.com</c> and
/// <c>.corp.com</c> also cover subdomains, <c>*</c> covers everything, and a <c>:port</c> suffix is ignored.
/// </summary>
internal sealed class NoProxyList(IReadOnlyList<string> entries)
{
    public static NoProxyList Parse(string? value) => new(
        (value ?? string.Empty)
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.StartsWith('[') ? entry : entry.Split(':')[0])      // drop a port, keep [ipv6]
            .Select(entry => entry.TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(entry => entry.Length > 0 || value?.Trim() == "*")
            .ToList());

    public bool Matches(string host)
    {
        var name = host.Trim('[', ']').ToLowerInvariant();
        return entries.Any(entry => entry.Length == 0
            || name == entry.Trim('[', ']')
            || name.EndsWith("." + entry, StringComparison.Ordinal));
    }
}
