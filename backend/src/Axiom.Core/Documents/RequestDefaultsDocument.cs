namespace Axiom.Documents;

/// <summary>
/// Defaults applied to every request step, as exchanged with the desktop app.
/// </summary>
public sealed class RequestDefaultsDocument
{
    /// <summary>
    /// Headers added to every request unless the step sets its own.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}