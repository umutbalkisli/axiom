using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Axiom.Network;

/// <summary>
/// The HTTP handler behind every call Axiom makes to an API (request steps, OpenAPI import, secret stores), set up
/// from <see cref="NetworkSettings"/>.
/// </summary>
public static class AxiomHttp
{
    /// <summary>
    /// A handler that goes through the configured proxy, signs in to it with the current user's credentials when it
    /// asks (Windows NTLM / Kerberos), and also trusts the extra root certificates.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(NetworkSettings settings)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = settings.Proxy is not null,
            Proxy = settings.Proxy is SchemeProxy scheme ? new SignInPerScheme(scheme) : settings.Proxy,
            // Used when the proxy (system or HTTPS_PROXY) has no credentials of its own: the signed-in user.
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            // Pick up proxy and DNS changes (VPN on/off) in long runs.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (settings.ExtraRoots.Count > 0)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                IsTrusted(certificate as X509Certificate2 ?? (certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData())), errors, settings.ExtraRoots);
        }

        return handler;
    }

    /// <summary>
    /// A certificate the system already trusts is fine. One that fails only because its chain ends in an unknown root
    /// is fine too when that root is one of <paramref name="extraRoots"/> (a company's HTTPS-inspecting proxy). A wrong
    /// name or an expired certificate is never accepted.
    /// </summary>
    public static bool IsTrusted(X509Certificate2? certificate, SslPolicyErrors errors, X509Certificate2Collection extraRoots)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || errors != SslPolicyErrors.RemoteCertificateChainErrors || extraRoots.Count == 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(extraRoots);
        chain.ChainPolicy.ExtraStore.AddRange(extraRoots);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    /// <summary>
    /// The handler asks a proxy for its credentials without saying for which request; this hands it the sign-in of
    /// the proxy the current request goes through (HTTPS_PROXY and HTTP_PROXY may differ).
    /// </summary>
    private sealed class SignInPerScheme(SchemeProxy proxy) : IWebProxy, ICredentials
    {
        public ICredentials? Credentials
        {
            get => this;
            set { }
        }

        public Uri? GetProxy(Uri destination) => proxy.GetProxy(destination);

        public bool IsBypassed(Uri host) => proxy.IsBypassed(host);

        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            // uri is the proxy's address here; find the scheme proxy it belongs to by trying both schemes.
            foreach (var scheme in new[] { "https", "http" })
            {
                var sample = new Uri($"{scheme}://example.invalid/");
                if (proxy.GetProxy(sample) is { } address && address.Host == uri.Host && address.Port == uri.Port)
                {
                    return proxy.CredentialsFor(sample)?.GetCredential(uri, authType);
                }
            }

            return CredentialCache.DefaultCredentials.GetCredential(uri, authType);
        }
    }
}
