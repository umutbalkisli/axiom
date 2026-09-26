using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Axiom.Tests.Support;

/// <summary>
/// A tiny local HTTP server for provider tests (Vault, Kubernetes) that need a real socket.
/// </summary>
public sealed class FakeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;

    public FakeServer(Func<string, IReadOnlyDictionary<string, string>, (int Status, string Body)> handle)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                var headers = context.Request.Headers.AllKeys.Where(k => k is not null)
                    .ToDictionary(k => k!, k => context.Request.Headers[k]!, StringComparer.OrdinalIgnoreCase);
                var (status, body) = handle(context.Request.Url!.AbsolutePath, headers);
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        });
    }

    public string Url { get; }

    public void Dispose()
    {
        _listener.Close();
        try
        {
            _loop.Wait(1000);
        }
        catch (AggregateException)
        {
            // the listener was closed while waiting for a request
        }
    }
}

/// <summary>
/// Sets environment variables for the duration of a test and restores them afterwards.
/// </summary>
public sealed class EnvironmentScope : IDisposable
{
    private readonly Dictionary<string, string?> _original = [];

    public EnvironmentScope Set(string name, string? value)
    {
        _original.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
        return this;
    }

    public void Dispose()
    {
        foreach (var (name, value) in _original)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}

/// <summary>
/// Test classes that touch process-wide environment variables must not run in parallel.
/// </summary>
[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;
