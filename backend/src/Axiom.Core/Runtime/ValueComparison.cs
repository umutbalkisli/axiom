using System.Globalization;
using System.Text.Json;

namespace Axiom.Runtime;

/// <summary>Loose comparison rules shared by assertion operators: numbers compare numerically, everything else as case-insensitive text.</summary>
internal static class ValueComparison
{
    public static bool AreEqual(object? left, object? right)
    {
        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static int Compare(object? left, object? right)
    {
        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left?.ToString(), right?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsText(object? actual, object? expected) =>
        actual?.ToString()?.Contains(expected?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;

    internal static bool TryToDecimal(object? value, out decimal result)
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
            case JsonElement { ValueKind: JsonValueKind.Number } number:
                return number.TryGetDecimal(out result);
            default:
                return decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
