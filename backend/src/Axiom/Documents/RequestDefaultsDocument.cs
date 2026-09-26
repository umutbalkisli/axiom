namespace Axiom.Documents;

public sealed class RequestDefaultsDocument
{
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}