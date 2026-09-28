using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Axiom.Network;

namespace Axiom.Tests.Network;

public class ProxyTests
{
    [Fact]
    public async Task Requests_go_through_a_configured_proxy()
    {
        using var proxy = new FakeProxy();
        using var client = new HttpClient(AxiomHttp.CreateHandler(new NetworkSettings(proxy.Address)));

        var body = await client.GetStringAsync("http://api.example.test/users/1");

        Assert.Equal("via proxy", body);
        Assert.Equal("GET http://api.example.test/users/1 HTTP/1.1", proxy.RequestLines.Single());
    }

    [Fact]
    public async Task A_user_name_and_password_in_the_proxy_address_answer_its_sign_in_request()
    {
        using var proxy = new FakeProxy(requireBasic: "user:p@ss w0rd");
        var address = proxy.Address.Replace("http://", "http://user:p%40ss%20w0rd@");
        var settings = new NetworkSettings(address);
        using var client = new HttpClient(AxiomHttp.CreateHandler(settings));

        var body = await client.GetStringAsync("http://api.example.test/");

        Assert.Equal("via proxy", body);
        Assert.Equal(2, proxy.RequestLines.Count);                        // 407 first, then signed in
        Assert.DoesNotContain("p@ss", settings.ProxySource);                // the password never shows in messages
        Assert.Contains("user:***@", settings.ProxySource);
    }

    [Fact]
    public async Task A_proxy_that_asks_for_a_sign_in_it_does_not_get_is_explained()
    {
        using var proxy = new FakeProxy(requireBasic: "user:secret");
        var settings = new NetworkSettings(proxy.Address);
        using var client = new HttpClient(AxiomHttp.CreateHandler(settings));
        var target = new Uri("https://api.example.test/");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(target));
        var message = NetworkErrors.Describe(error, target, settings);

        Assert.Contains($"went through proxy {proxy.Address}", message);
        Assert.Contains("407", message);
        Assert.Contains("http://user:password@proxy:8080", message);        // what to do about it
    }

    [Fact]
    public async Task A_request_step_reports_a_proxy_sign_in_demand_instead_of_a_failed_check()
    {
        using var proxy = new FakeProxy(requireBasic: "user:secret");
        var settings = new NetworkSettings(proxy.Address);
        var executor = new RequestStepExecutor(new HttpClient(AxiomHttp.CreateHandler(settings)), Build.Engine(), settings);
        var runner = new StepRunner([executor]);
        var step = Build.Request("r", "http://api.example.test/", assertions: Build.Assertion("status", "==", 200));

        var result = (await runner.RunAsync(Build.Context(Build.Collection(), runner), [step], default)).Single();

        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("407", result.Error);
        Assert.StartsWith($"The proxy {proxy.Address} turned the request", result.Error);
    }

    [Fact]
    public void None_goes_direct_and_loopback_never_uses_the_proxy()
    {
        var none = new NetworkSettings("none");
        var proxied = new NetworkSettings("http://proxy.corp:8080", noProxy: ".internal.corp, api.test:8443, 10.1.2.3");

        Assert.Null(none.ProxyFor(new Uri("https://api.example.com/")));
        Assert.Equal("none", none.ProxySource);
        Assert.Equal(new Uri("http://proxy.corp:8080/"), proxied.ProxyFor(new Uri("https://api.example.com/")));
        Assert.Null(proxied.ProxyFor(new Uri("http://127.0.0.1:5000/")));
        Assert.Null(proxied.ProxyFor(new Uri("http://localhost/")));
    }

    [Theory]
    [InlineData("https://svc.internal.corp/", true)]
    [InlineData("https://internal.corp/", true)]
    [InlineData("https://notinternal.corp/", false)]
    [InlineData("https://api.test/", true)]
    [InlineData("https://sub.api.test/", true)]
    [InlineData("http://10.1.2.3/", true)]
    [InlineData("https://example.com/", false)]
    public void No_proxy_entries_cover_the_name_and_its_subdomains(string url, bool direct)
    {
        var settings = new NetworkSettings("http://proxy.corp:8080", noProxy: ".internal.corp, api.test:8443, 10.1.2.3");
        Assert.Equal(direct, settings.ProxyFor(new Uri(url)) is null);
    }

    [Fact]
    public void A_star_in_no_proxy_sends_everything_direct()
    {
        Assert.Null(new NetworkSettings("http://proxy.corp:8080", noProxy: "*").ProxyFor(new Uri("https://example.com/")));
    }

    [Theory]
    [InlineData("ftp://proxy:21")]
    [InlineData("http://")]
    public void A_proxy_setting_that_is_not_an_address_is_rejected_with_the_accepted_forms(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new NetworkSettings(value));
        Assert.Contains("'system', 'none' or a proxy address", error.Message);
    }
    [Fact]
    public async Task Https_proxy_variables_sign_in_on_the_tunnel_and_never_show_the_password()
    {
        using var proxy = new FakeProxy(requireBasic: "alice:s3cret");
        var address = proxy.Address.Replace("http://", "http://alice:s3cret@");
        var settings = new NetworkSettings(httpsProxy: address, noProxy: "internal.corp");
        using var client = new HttpClient(AxiomHttp.CreateHandler(settings));
        var target = new Uri("https://api.example.test/");

        // The fake proxy accepts the sign-in, then refuses the tunnel itself (502): enough to see both requests.
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(target));
        var message = NetworkErrors.Describe(error, target, settings);

        Assert.Equal(["CONNECT api.example.test:443 HTTP/1.1", "CONNECT api.example.test:443 HTTP/1.1"], proxy.RequestLines);
        Assert.Equal("HTTPS_PROXY / HTTP_PROXY", settings.ProxySource);
        Assert.Contains($"through proxy http://127.0.0.1:{new Uri(proxy.Address).Port}", message);
        Assert.DoesNotContain("s3cret", message);
        Assert.DoesNotContain("alice", message);
        Assert.Null(settings.ProxyFor(new Uri("https://svc.internal.corp/")));            // curl-style NO_PROXY here too
        Assert.Null(settings.ProxyFor(new Uri("http://api.example.test/")));               // no HTTP_PROXY: http goes direct
    }

    [Fact]
    public void A_proxy_is_always_shown_without_its_user_name_and_password()
    {
        Assert.Equal("http://proxy.corp:8080", NetworkSettings.Display(new Uri("http://bob:hunter2@proxy.corp:8080/")));
        Assert.Equal("http://bob:***@proxy.corp:8080/", ExplicitProxyAddress.Redact("http://bob:hunter2@proxy.corp:8080"));
    }
}

public class CompanyCertificateTests
{
    private static (X509Certificate2 Root, X509Certificate2 Leaf) Chain(string name)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest($"CN={name} Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=api.example.test", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30), [1, 2, 3, 4]);
        return (root, leaf);
    }

    [Fact]
    public void A_certificate_from_the_company_root_is_trusted_only_when_that_root_is_configured()
    {
        var (root, leaf) = Chain("Corp");
        var (otherRoot, _) = Chain("Other");

        Assert.True(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.RemoteCertificateChainErrors, [root]));
        Assert.False(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.RemoteCertificateChainErrors, []));
        Assert.False(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.RemoteCertificateChainErrors, [otherRoot]));
    }

    [Fact]
    public void A_wrong_host_name_is_never_accepted_even_from_a_trusted_root()
    {
        var (root, leaf) = Chain("Corp");

        Assert.False(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.RemoteCertificateNameMismatch, [root]));
        Assert.False(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, [root]));
        Assert.True(AxiomHttp.IsTrusted(leaf, SslPolicyErrors.None, []));      // trusted by the system: nothing to add
    }

    [Fact]
    public void The_company_certificate_file_is_read_as_pem_and_problems_are_explained()
    {
        using var folder = new TempFolder();
        var (root, _) = Chain("Corp");
        var pem = folder.Write("corp.pem", root.ExportCertificatePem());
        var empty = folder.Write("empty.pem", "no certificate here");

        Assert.Single(new NetworkSettings(caCertificatesFile: pem).ExtraRoots);
        Assert.Contains("does not exist", Assert.Throws<InvalidOperationException>(() => new NetworkSettings(caCertificatesFile: Path.Combine(folder.Path, "missing.pem"))).Message);
        Assert.Contains("contains no certificate", Assert.Throws<InvalidOperationException>(() => new NetworkSettings(caCertificatesFile: empty)).Message);
    }
}

/// <summary>
/// A minimal HTTP proxy on 127.0.0.1: answers plain requests with "via proxy", refuses CONNECT tunnels it has not
/// been signed in to, and, with <c>requireBasic</c>, asks for Basic sign-in (407) like a company proxy.
/// </summary>
internal sealed class FakeProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string? _expectedCredentials;
    private readonly List<string> _requestLines = [];

    public FakeProxy(string? requireBasic = null)
    {
        _expectedCredentials = requireBasic is null ? null : "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(requireBasic));
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public string Address => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public IReadOnlyList<string> RequestLines
    {
        get
        {
            lock (_requestLines)
            {
                return _requestLines.ToList();
            }
        }
    }

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleAsync(client));
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // stopped
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (await reader.ReadLineAsync() is { Length: > 0 } requestLine)
            {
                string? authorization = null;
                while (await reader.ReadLineAsync() is { Length: > 0 } header)
                {
                    if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                    {
                        authorization = header["Proxy-Authorization:".Length..].Trim();
                    }
                }

                lock (_requestLines)
                {
                    _requestLines.Add(requestLine);
                }

                string response;
                if (_expectedCredentials is not null && authorization != _expectedCredentials)
                {
                    response = "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"corp\"\r\nContent-Length: 0\r\n\r\n";
                }
                else if (requestLine.StartsWith("CONNECT", StringComparison.Ordinal))
                {
                    response = "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else
                {
                    response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 9\r\n\r\nvia proxy";
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                await stream.FlushAsync();
            }
        }
    }
}
