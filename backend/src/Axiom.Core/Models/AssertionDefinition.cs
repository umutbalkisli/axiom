using Axiom.Defaults;

namespace Axiom.Models;

public sealed class AssertionDefinition
{
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    /// <summary>Optional aggregation (count, sum, avg, min, max) applied to the resolved value before the comparison.</summary>
    public string? Aggregate { get; set; }
    public string Operator { get; set; } = AssertionDefaults.OperatorEquals;
    public object? Expected { get; set; }
}