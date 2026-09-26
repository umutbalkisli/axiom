namespace Axiom.Runtime;

/// <summary>
/// The operators that ship with Axiom.
/// </summary>
public static class BuiltInAssertionOperators
{
    /// <summary>
    /// Every built-in operator.
    /// </summary>
    public static IReadOnlyList<IAssertionOperator> All { get; } =
    [
        // equality and ordering
        new Operator("==", (actual, expected, o) => ValueComparison.AreEqual(actual, expected, o)),
        new Operator("!=", (actual, expected, o) => !ValueComparison.AreEqual(actual, expected, o)),
        new Operator(">", (actual, expected, o) => ValueComparison.Compare(actual, expected, o) > 0),
        new Operator(">=", (actual, expected, o) => ValueComparison.Compare(actual, expected, o) >= 0),
        new Operator("<", (actual, expected, o) => ValueComparison.Compare(actual, expected, o) < 0),
        new Operator("<=", (actual, expected, o) => ValueComparison.Compare(actual, expected, o) <= 0),
        new Operator("approx", (actual, expected, o) => ValueComparison.IsApproximately(actual, expected, o)),

        // text
        new Operator("contains", (actual, expected, o) => ValueComparison.ContainsText(actual, expected, o), searchesRawText: true),
        new Operator("not_contains", (actual, expected, o) => !ValueComparison.ContainsText(actual, expected, o), searchesRawText: true),
        new Operator("starts_with", (actual, expected, o) => ValueComparison.StartsWith(actual, expected, o)),
        new Operator("ends_with", (actual, expected, o) => ValueComparison.EndsWith(actual, expected, o)),
        new Operator("matches", (actual, expected, o) => ValueComparison.Matches(actual, expected, o)),

        // membership
        new Operator("in", (actual, expected, o) => ValueComparison.IsOneOf(actual, expected, o)),
        new Operator("not_in", (actual, expected, o) => !ValueComparison.IsOneOf(actual, expected, o)),

        // presence and type
        new Operator("exists", (actual, _, _) => actual is not null, handlesMissing: true),
        new Operator("not_exists", (actual, _, _) => actual is null, handlesMissing: true),
        new Operator("is_null", (actual, _, o) => !o.IsMissing && ValueComparison.IsNull(actual), handlesMissing: true),
        new Operator("is_missing", (_, _, o) => o.IsMissing, handlesMissing: true),
        new Operator("is_empty", (actual, _, _) => ValueComparison.IsEmpty(actual), handlesMissing: true),
        new Operator("is_not_empty", (actual, _, _) => !ValueComparison.IsEmpty(actual), handlesMissing: true),
        new Operator("is_type", (actual, expected, o) => ValueComparison.HasType(actual, expected, o)),
    ];

    private sealed class Operator(string name, Func<object?, object?, ComparisonOptions, bool> evaluate, bool searchesRawText = false, bool handlesMissing = false) : IAssertionOperator
    {
        public string Name { get; } = name;

        public bool SearchesRawText { get; } = searchesRawText;

        public bool HandlesMissing { get; } = handlesMissing;

        public bool Evaluate(object? actual, object? expected, ComparisonOptions options) => evaluate(actual, expected, options);
    }
}
