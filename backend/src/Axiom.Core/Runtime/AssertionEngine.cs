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
            if (source is null && !HandlesMissing(assertion))
            {
                // Nothing by that name exists, so the assertion cannot be evaluated (usually a typo in the source).
                results.Add(Build(assertion, assertion.Expected, null, RunOutcome.Error,
                    $"Unknown source '{assertion.Source}'. Check the spelling, or that an earlier step saves a result with that name."));
                continue;
            }

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
        var operatorName = assertion.Operator.Trim();
        var expected = assertion.Expected;

        // A comparison answers yes or no. Anything that throws while getting there (a bad operator, an unresolved
        // {{variable}}, a pattern that is not a regex, ...) means the assertion could not be evaluated: that is an
        // error in the test, reported apart from an assertion that ran and failed.
        try
        {
            expected = ResolveExpected(assertion.Expected, context);

            if (!_operators.TryGetValue(operatorName, out var comparison))
            {
                throw new InvalidOperationException($"Unsupported operator '{operatorName}'");
            }

            var actual = Aggregate(assertion.Aggregate, actualValue);
            var passed = comparison.Evaluate(actual, expected, options);
            if (passed)
            {
                return Build(assertion, expected, actual, RunOutcome.Passed, null);
            }

            var message = $"Expected '{operatorName}' with value '{ValueComparison.Preview(expected)}', actual '{ValueComparison.Preview(actual)}'";
            if (!found && !comparison.HandlesMissing)
            {
                message += $" ('{assertion.Source}{(string.IsNullOrEmpty(assertion.Path) ? string.Empty : "." + assertion.Path)}' was not found)";
            }

            return Build(assertion, expected, actual, RunOutcome.Failed, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Build(assertion, expected, actualValue, RunOutcome.Error, ex.Message);
        }
    }

    private bool HandlesMissing(AssertionDefinition assertion) =>
        _operators.TryGetValue(assertion.Operator.Trim(), out var comparison) && comparison.HandlesMissing;

    private static AssertionResult Build(AssertionDefinition assertion, object? expected, object? actual, RunOutcome outcome, string? error) => new()
    {
        Source = assertion.Source,
        Path = assertion.Path,
        Aggregate = assertion.Aggregate,
        Operator = assertion.Operator.Trim(),
        // A list given as the expected value (for example for "in") is shown as JSON rather than its type name.
        Expected = expected is System.Collections.IEnumerable and not string ? JsonSerializer.Serialize(expected) : expected,
        // A list collected by a wildcard path is shown as JSON rather than its type name.
        Actual = actual is List<object?> list ? JsonSerializer.Serialize(list) : actual,
        Outcome = outcome,
        Error = error,
    };

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
