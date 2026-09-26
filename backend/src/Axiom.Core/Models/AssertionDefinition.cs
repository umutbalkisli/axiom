using Axiom.Defaults;

namespace Axiom.Models;

/// <summary>
/// A check on a value produced by a step.
/// </summary>
public sealed class AssertionDefinition
{
    /// <summary>
    /// Where the value comes from: <c>status</c>, <c>body</c>, <c>headers</c>, a saved result name, and so on.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// Dot-separated path into the source (<c>items.0.name</c>, <c>items.*.price</c>, <c>@http.status</c>); empty for the whole source.
    /// </summary>
    public string? Path { get; set; }
    /// <summary>
    /// Optional aggregation (count, sum, avg, min, max) applied to the resolved value before the comparison.
    /// </summary>
    public string? Aggregate { get; set; }
    /// <summary>
    /// No type coercion when comparing (text "200" no longer equals number 200).
    /// </summary>
    public bool Strict { get; set; }

    /// <summary>
    /// Text comparisons distinguish upper and lower case.
    /// </summary>
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Allowed difference for the <c>approx</c> operator.
    /// </summary>
    public decimal? Tolerance { get; set; }

    /// <summary>
    /// The comparison to make, such as <c>==</c>, <c>contains</c> or <c>matches</c>.
    /// </summary>
    public string Operator { get; set; } = AssertionDefaults.OperatorEquals;
    
    /// <summary>
    /// The value to compare against; text may contain <c>{{variables}}</c>.
    /// </summary>
    public object? Expected { get; set; }
}