using Axiom.Defaults;

namespace Axiom.Models;

public sealed class AssertionDefinition
{
    public string Source { get; set; } = string.Empty;
    public string? Path { get; set; }
    /// <summary>Optional aggregation (count, sum, avg, min, max) applied to the resolved value before the comparison.</summary>
    public string? Aggregate { get; set; }
    /// <summary>No type coercion when comparing (text "200" no longer equals number 200).</summary>
    public bool Strict { get; set; }

    /// <summary>Text comparisons distinguish upper and lower case.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Allowed difference for the <c>approx</c> operator.</summary>
    public decimal? Tolerance { get; set; }

    public string Operator { get; set; } = AssertionDefaults.OperatorEquals;
    public object? Expected { get; set; }
}