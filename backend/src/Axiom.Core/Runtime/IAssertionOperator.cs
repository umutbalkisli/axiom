namespace Axiom.Runtime;

/// <summary>
/// A comparison usable in an assertion's <c>operator</c> field. Register implementations to add new operators.</summary>
public interface IAssertionOperator
{
    string Name { get; }

    /// <summary>
    /// True for text searches. When such an operator is applied to a whole response body (no path, no aggregation),
    /// the engine searches the raw response text, which is far cheaper than walking a parsed JSON tree.
    /// </summary>
    bool SearchesRawText => false;

    bool Evaluate(object? actual, object? expected, ComparisonOptions options);
}
