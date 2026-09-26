using Axiom.Defaults;

namespace Axiom.Documents;

public sealed class AssertionDocument
{
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    /// <summary>Optional aggregation applied to the resolved value; null (omitted from YAML) means none.</summary>
    public string? Aggregate { get; set; }
    public bool? Strict { get; set; }
    public bool? CaseSensitive { get; set; }
    public decimal? Tolerance { get; set; }
    public string Operator { get; set; } = AssertionDefaults.OperatorEquals;
    public object? Expected { get; set; }
}