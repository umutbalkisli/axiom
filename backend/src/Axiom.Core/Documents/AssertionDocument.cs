using Axiom.Defaults;

namespace Axiom.Documents;

/// <summary>
/// An assertion as exchanged with the desktop app and written to a test file.
/// </summary>
public sealed class AssertionDocument
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
    /// Optional aggregation applied to the resolved value; null (omitted from YAML) means none.
    /// </summary>
    public string? Aggregate { get; set; }
    /// <summary>
    /// When true, no type conversion is applied (the text "200" is not the number 200). Null means off.
    /// </summary>
    public bool? Strict { get; set; }
    /// <summary>
    /// When true, text comparisons distinguish upper and lower case. Null means off.
    /// </summary>
    public bool? CaseSensitive { get; set; }
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