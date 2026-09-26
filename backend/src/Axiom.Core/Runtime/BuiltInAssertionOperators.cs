namespace Axiom.Runtime;

public static class BuiltInAssertionOperators
{
    public static IReadOnlyList<IAssertionOperator> All { get; } =
    [
        new Operator("==", (actual, expected) => ValueComparison.AreEqual(actual, expected)),
        new Operator("!=", (actual, expected) => !ValueComparison.AreEqual(actual, expected)),
        new Operator(">", (actual, expected) => ValueComparison.Compare(actual, expected) > 0),
        new Operator(">=", (actual, expected) => ValueComparison.Compare(actual, expected) >= 0),
        new Operator("<", (actual, expected) => ValueComparison.Compare(actual, expected) < 0),
        new Operator("<=", (actual, expected) => ValueComparison.Compare(actual, expected) <= 0),
        new Operator("contains", (actual, expected) => ValueComparison.ContainsText(actual, expected), searchesRawText: true),
        new Operator("not_contains", (actual, expected) => !ValueComparison.ContainsText(actual, expected), searchesRawText: true),
        new Operator("exists", (actual, _) => actual is not null),
        new Operator("not_exists", (actual, _) => actual is null),
    ];

    private sealed class Operator(string name, Func<object?, object?, bool> evaluate, bool searchesRawText = false) : IAssertionOperator
    {
        public string Name { get; } = name;

        public bool SearchesRawText { get; } = searchesRawText;

        public bool Evaluate(object? actual, object? expected) => evaluate(actual, expected);
    }
}
