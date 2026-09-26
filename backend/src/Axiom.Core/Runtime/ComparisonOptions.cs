namespace Axiom.Runtime;

/// <summary>Per-assertion switches that change how an operator compares values.</summary>
/// <param name="Strict">No type coercion: a text "200" is not equal to the number 200, and ordering needs two numbers or two texts.</param>
/// <param name="CaseSensitive">Text comparisons (equality, contains, starts_with, in, matches, ...) distinguish upper and lower case.</param>
/// <param name="Tolerance">Allowed difference for the <c>approx</c> operator.</param>
/// <param name="IsMissing">The path did not exist at all, as opposed to being present with a null value.</param>
public readonly record struct ComparisonOptions(bool Strict = false, bool CaseSensitive = false, decimal Tolerance = 0, bool IsMissing = false)
{
    public StringComparison Text => CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
