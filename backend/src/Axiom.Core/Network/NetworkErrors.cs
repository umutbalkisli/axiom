using System.Net;
using System.Security.Authentication;

namespace Axiom.Network;

/// <summary>
/// Turns a failed HTTP call into a message that says which way it went (direct or through which proxy) and, for the
/// usual problems behind a company proxy, what to do about it.
/// </summary>
public static class NetworkErrors
{
    /// <summary>
    /// Describes <paramref name="exception"/>, raised by a call to <paramref name="uri"/>.
    /// </summary>
    public static string Describe(HttpRequestException exception, Uri uri, NetworkSettings settings)
    {
        var proxy = settings.ProxyFor(uri);
        var route = proxy is null ? "direct" : $"through proxy {NetworkSettings.Display(proxy)}";
        var details = string.Join(" ", Messages(exception));

        string? hint = null;
        if (exception.StatusCode == HttpStatusCode.ProxyAuthenticationRequired || details.Contains("407", StringComparison.Ordinal))
        {
            hint = ProxySignInHint;
        }
        else if (exception.HttpRequestError == HttpRequestError.SecureConnectionError || exception.InnerException is AuthenticationException)
        {
            hint = details.Contains("UntrustedRoot", StringComparison.OrdinalIgnoreCase) || details.Contains("PartialChain", StringComparison.OrdinalIgnoreCase)
                ? "The server's certificate is signed by an authority this machine does not trust. If your company's proxy inspects HTTPS, it shows its own certificate: point AXIOM_CA_CERTS to that certificate (a PEM file)."
                : "The secure (HTTPS) connection could not be set up.";
        }
        else if (exception.HttpRequestError == HttpRequestError.NameResolutionError)
        {
            hint = proxy is null
                ? $"The name '{uri.Host}' could not be found. Behind a company proxy, outside names are often only reachable through it: check the proxy (AXIOM_PROXY, or the system's proxy settings)."
                : $"The name of the proxy or of '{uri.Host}' could not be found.";
        }
        else if (exception.HttpRequestError == HttpRequestError.ConnectionError && proxy is not null)
        {
            hint = $"Could not connect to the proxy {NetworkSettings.Display(proxy)}. Check that it is right (AXIOM_PROXY, HTTPS_PROXY or the system's proxy settings), or use AXIOM_PROXY=none for a network without a proxy.";
        }
        else if (exception.HttpRequestError == HttpRequestError.ProxyTunnelError)
        {
            hint = "The proxy refused to open a connection to the server.";
        }

        return $"{details} (request to {uri.GetLeftPart(UriPartial.Authority)} went {route}){(hint is null ? string.Empty : " " + hint)}";
    }

    /// <summary>
    /// A <c>407</c> answer that came through a proxy: the proxy, not the API, turned the request away.
    /// </summary>
    public static string DescribeProxySignIn(Uri uri, Uri proxy) =>
        $"The proxy {NetworkSettings.Display(proxy)} turned the request to {uri.GetLeftPart(UriPartial.Authority)} away: 407 Proxy Authentication Required. {ProxySignInHint}";

    private const string ProxySignInHint =
        "The proxy wants you to sign in (407). Axiom sends your Windows sign-in by itself; if the proxy needs a user name and password, put them in its address: AXIOM_PROXY or HTTPS_PROXY=http://user:password@proxy:8080.";

    /// <summary>
    /// The messages of the exception and its causes, leaving out one that an earlier one already says.
    /// </summary>
    private static List<string> Messages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message.Trim();
            if (message.Length > 0 && !messages.Any(earlier => earlier.Contains(message, StringComparison.Ordinal)))
            {
                messages.Add(message);
            }
        }

        return messages;
    }
}
