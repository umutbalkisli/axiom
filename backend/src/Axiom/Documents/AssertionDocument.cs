using Axiom.Defaults;

namespace Axiom.Documents;

public sealed class AssertionDocument
{
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string Operator { get; set; } = AssertionDefaults.OperatorEquals;
    public object? Expected { get; set; }
}