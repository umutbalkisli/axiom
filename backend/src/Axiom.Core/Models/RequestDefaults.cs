namespace Axiom.Models;

/// <summary>
/// Defaults applied to every request step of a collection.
/// </summary>
public sealed class RequestDefaults
{
    /// <summary>
    /// Headers added to every request unless the step sets its own.
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}