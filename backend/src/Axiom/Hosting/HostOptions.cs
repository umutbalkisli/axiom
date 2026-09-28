using Axiom.LocalSecrets;

namespace Axiom.Hosting;

/// <summary>
/// How the local host runs.
/// </summary>
internal sealed record HostOptions
{
    /// <summary>
    /// The port on 127.0.0.1; 0 picks a free one.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The secret every request must carry: as <c>Authorization: Bearer</c>, or (with <see cref="Ui"/>) as the session
    /// cookie the browser gets by opening <c>/?token=</c> once.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Serve the web UI and keep the host alive only while a UI is open.
    /// </summary>
    public bool Ui { get; init; }

    /// <summary>
    /// Serve the UI from this folder instead of the copy built into the executable (for UI development).
    /// </summary>
    public string? UiDirectory { get; init; }

    /// <summary>
    /// Stop when no UI has been heard from for this long (browsers slow background timers down to once a minute).
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// After a UI says goodbye (its window closed), stop unless it comes back (a reload) within this time.
    /// </summary>
    public TimeSpan GoodbyeGrace { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Stop when no UI has connected at all within this time (for example the browser could not be opened).
    /// </summary>
    public TimeSpan FirstConnectTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where preferences and the list of local secrets are kept; the user's app data folder unless a test says otherwise.
    /// </summary>
    public string? DataDirectory { get; init; }

    /// <summary>
    /// Where local secret values are kept; the operating system's secure storage unless a test says otherwise.
    /// </summary>
    public ISecureStorage? SecureStorage { get; init; }
}
