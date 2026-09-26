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
        new Operator("contains", (actual, expected) => ValueComparison.ContainsText(actual, expected)),
        new Operator("not_contains", (actual, expected) => !ValueComparison.ContainsText(actual, expected)),
        new Operator("exists", (actual, _) => actual is not null),
        new Operator("not_exists", (actual, _) => actual is null),
    ];

    private sealed class Operator(string name, Func<object?, object?, bool> evaluate) : IAssertionOperator
    {
        public string Name { get; } = name;

        public bool Evaluate(object? actual, object? expected) => evaluate(actual, expected);
    }
}
