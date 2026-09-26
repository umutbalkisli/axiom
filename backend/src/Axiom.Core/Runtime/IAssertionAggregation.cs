namespace Axiom.Runtime;

/// <summary>Reduces a list (or object) to one value before an assertion compares it. Register implementations to add new ones.</summary>
public interface IAssertionAggregation
{
    /// <summary>The <c>aggregate</c> value in YAML (case-insensitive).</summary>
    string Name { get; }

    /// <summary>Throws <see cref="InvalidOperationException"/> with a readable message when the value cannot be aggregated.</summary>
    object Apply(object? value);
}
