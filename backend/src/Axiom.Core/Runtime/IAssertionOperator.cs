namespace Axiom.Runtime;

/// <summary>
/// A comparison usable in an assertion's <c>operator</c> field. Register implementations to add new operators.</summary>
public interface IAssertionOperator
{
    string Name { get; }

    bool Evaluate(object? actual, object? expected);
}
