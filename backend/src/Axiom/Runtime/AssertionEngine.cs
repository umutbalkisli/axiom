using Axiom.Models;

namespace Axiom.Runtime;

public static class AssertionEngine
{
    public static AssertionResult Evaluate(AssertionDefinition assertion, object? actualValue, IReadOnlyDictionary<string, object?> context)
    {
        var expected = ResolveExpected(assertion.Expected, context);
        var @operator = assertion.Operator.Trim();

        try
        {
            var passed = @operator switch
            {
                "==" => EqualsNormalized(actualValue, expected),
                "!=" => !EqualsNormalized(actualValue, expected),
                ">" => Compare(actualValue, expected) > 0,
                ">=" => Compare(actualValue, expected) >= 0,
                "<" => Compare(actualValue, expected) < 0,
                "<=" => Compare(actualValue, expected) <= 0,
                "contains" => actualValue?.ToString()?.Contains(expected?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
                "not_contains" => actualValue?.ToString()?.Contains(expected?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) != true,
                "exists" => actualValue is not null,
                "not_exists" => actualValue is null,
                _ => throw new InvalidOperationException($"Unsupported operator '{@operator}'"),
            };

            return new AssertionResult
            {
                Source = assertion.Source,
                Operator = @operator,
                Expected = expected,
                Actual = actualValue,
                Passed = passed,
                Error = passed ? null : $"Expected '{@operator}' with value '{expected}', actual '{actualValue}'",
            };
        }
        catch (Exception ex)
        {
            return new AssertionResult
            {
                Source = assertion.Source,
                Operator = @operator,
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

    private static bool EqualsNormalized(object? left, object? right)
    {
        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Compare(object? left, object? right)
    {
        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case decimal d:
                result = d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case double db:
                result = (decimal)db;
                return true;
            case float f:
                result = (decimal)f;
                return true;
            default:
                return decimal.TryParse(value.ToString(), out result);
        }
    }
}