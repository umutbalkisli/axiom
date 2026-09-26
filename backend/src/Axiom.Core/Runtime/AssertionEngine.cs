using Axiom.Models;

namespace Axiom.Runtime;

public sealed class AssertionEngine
{
    private readonly Dictionary<string, IAssertionOperator> _operators;

    public AssertionEngine(IEnumerable<IAssertionOperator> operators)
    {
        _operators = operators.ToDictionary(o => o.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> OperatorNames => _operators.Keys;

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

        try
        {
            if (!_operators.TryGetValue(operatorName, out var comparison))
            {
                throw new InvalidOperationException($"Unsupported operator '{operatorName}'");
            }

            var passed = comparison.Evaluate(actualValue, expected);

            return new AssertionResult
            {
                Source = assertion.Source,
                Operator = operatorName,
                Expected = expected,
                Actual = actualValue,
                Passed = passed,
                Error = passed ? null : $"Expected '{operatorName}' with value '{expected}', actual '{actualValue}'",
            };
        }
        catch (Exception ex)
        {
            return new AssertionResult
            {
                Source = assertion.Source,
                Operator = operatorName,
                Expected = expected,
                Actual = actualValue,
                Passed = false,
                Error = ex.Message,
            };
        }
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
