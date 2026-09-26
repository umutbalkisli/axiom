using System.Text.Json;
using Axiom.Models;

namespace Axiom.Runtime;

public sealed class AssertionEngine
{
    private readonly Dictionary<string, IAssertionOperator> _operators;
    private readonly Dictionary<string, IAssertionAggregation> _aggregations;

    public AssertionEngine(IEnumerable<IAssertionOperator> operators, IEnumerable<IAssertionAggregation> aggregations)
    {
        _operators = operators.ToDictionary(o => o.Name, StringComparer.Ordinal);
        _aggregations = aggregations.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> OperatorNames => _operators.Keys;

    public IReadOnlyCollection<string> AggregationNames => _aggregations.Keys;

    /// <summary>Evaluates every assertion of a step; <paramref name="sourceResolver"/> maps an assertion source name to its value.</summary>
    public IReadOnlyList<AssertionResult> EvaluateAll(
        StepDefinition step,
        IReadOnlyDictionary<string, object?> context,
        Func<string, object?> sourceResolver)
    {
        var results = new List<AssertionResult>(step.Assert.Count);
        foreach (var assertion in step.Assert)
        {
            var source = sourceResolver(assertion.Source);
            var actual = ReadValue(assertion, source, out var found);
            results.Add(Evaluate(assertion, actual, context, found));
        }

        return results;
    }

    /// <summary>The value an assertion looks at: the path applied to the source, or the raw text for a text search over a whole response body.</summary>
    private object? ReadValue(AssertionDefinition assertion, object? source, out bool found)
    {
        if (source is LazyJson body
            && string.IsNullOrWhiteSpace(assertion.Path)
            && string.IsNullOrWhiteSpace(assertion.Aggregate)
            && _operators.TryGetValue(assertion.Operator.Trim(), out var comparison)
            && comparison.SearchesRawText)
        {
            found = true;
            return body.Text;
        }

        found = TemplateResolver.TryResolveFrom(source, assertion.Path, out var value);
        return value;
    }

    /// <param name="found">False when the source or path did not exist (as opposed to being present with a null value).</param>
    public AssertionResult Evaluate(AssertionDefinition assertion, object? actualValue, IReadOnlyDictionary<string, object?> context, bool found = true)
    {
        var options = new ComparisonOptions(assertion.Strict, assertion.CaseSensitive, assertion.Tolerance ?? 0, IsMissing: !found);
        var expected = ResolveExpected(assertion.Expected, context);
        var operatorName = assertion.Operator.Trim();

        AssertionResult Result(object? actual, bool passed, string? error) => new()
        {
            Source = assertion.Source,
            Path = assertion.Path,
            Aggregate = assertion.Aggregate,
            Operator = operatorName,
            // A list given as the expected value (for example for "in") is shown as JSON rather than its type name.
            Expected = expected is System.Collections.IEnumerable and not string ? JsonSerializer.Serialize(expected) : expected,
            // A list collected by a wildcard path is shown as JSON rather than its type name.
            Actual = actual is List<object?> list ? JsonSerializer.Serialize(list) : actual,
            Passed = passed,
            Error = error,
        };

        try
        {
            if (!_operators.TryGetValue(operatorName, out var comparison))
            {
                throw new InvalidOperationException($"Unsupported operator '{operatorName}'");
            }

            var actual = Aggregate(assertion.Aggregate, actualValue);
            var passed = comparison.Evaluate(actual, expected, options);

            return Result(actual, passed, passed ? null : $"Expected '{operatorName}' with value '{ValueComparison.Preview(expected)}', actual '{ValueComparison.Preview(actual)}'");
        }
        catch (Exception ex)
        {
            return Result(actualValue, false, ex.Message);
        }
    }

    private object? Aggregate(string? name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return value;
        }

        return _aggregations.TryGetValue(name.Trim(), out var aggregation)
            ? aggregation.Apply(value)
            : throw new InvalidOperationException($"Unsupported aggregation '{name}'");
    }

    private static object? ResolveExpected(object? expected, IReadOnlyDictionary<string, object?> context)
    {
        if (expected is string text)
        {
            return TemplateResolver.ResolveString(text, context);
        }

        return expected;
    }
}
