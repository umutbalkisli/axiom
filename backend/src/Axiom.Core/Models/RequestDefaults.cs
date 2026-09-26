namespace Axiom.Models;

public sealed class RequestDefaults
{
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}