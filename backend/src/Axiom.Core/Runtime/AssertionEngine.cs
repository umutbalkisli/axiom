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
            var actual = TemplateResolver.ResolveFrom(sourceResolver(assertion.Source), assertion.Path);
            results.Add(Evaluate(assertion, actual, context));
        }

        return results;
    }

    public AssertionResult Evaluate(AssertionDefinition assertion, object? actualValue, IReadOnlyDictionary<string, object?> context)
    {
        var expected = ResolveExpected(assertion.Expected, context);
        var operatorName = assertion.Operator.Trim();

        AssertionResult Result(object? actual, bool passed, string? error) => new()
        {
            Source = assertion.Source,
            Path = assertion.Path,
            Aggregate = assertion.Aggregate,
            Operator = operatorName,
            Expected = expected,
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
            var passed = comparison.Evaluate(actual, expected);

            return Result(actual, passed, passed ? null : $"Expected '{operatorName}' with value '{expected}', actual '{actual}'");
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
