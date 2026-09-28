using Microsoft.Extensions.Hosting;

namespace Axiom.Hosting;

/// <summary>
/// Keeps the host alive while a UI is open. The UI pings regularly and says goodbye when its window closes; the host
/// stops when it has not heard from a UI for a while, when a goodbye is not followed by a ping (a reload pings again),
/// or when no UI connected at all. So closing the window ends the program, as closing a desktop app would.
/// </summary>
internal sealed class UiSession(HostOptions options, IHostApplicationLifetime lifetime, TimeProvider time) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly DateTimeOffset _started = time.GetUtcNow();
    private DateTimeOffset? _lastSeen;
    private DateTimeOffset? _goodbyeAt;
    private ITimer? _timer;

    public void Start() =>
        _timer = time.CreateTimer(_ => Check(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    public void Ping()
    {
        lock (_lock)
        {
            _lastSeen = time.GetUtcNow();
            _goodbyeAt = null;
        }
    }

    public void Goodbye()
    {
        lock (_lock)
        {
            _goodbyeAt = time.GetUtcNow();
        }
    }

    public void Dispose() => _timer?.Dispose();

    private void Check()
    {
        var now = time.GetUtcNow();
        bool stop;
        lock (_lock)
        {
            stop = _goodbyeAt is { } goodbye && now - goodbye >= options.GoodbyeGrace
                || _lastSeen is { } seen && now - seen >= options.IdleTimeout
                || _lastSeen is null && now - _started >= options.FirstConnectTimeout;
        }

        if (stop)
        {
            _timer?.Dispose();
            lifetime.StopApplication();
        }
    }
}
