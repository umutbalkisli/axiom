using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiom.Runtime;

/// <summary>
/// Comparison rules behind the built-in assertion operators. By default numbers compare numerically and everything
/// else as case-insensitive text; <see cref="ComparisonOptions"/> tightens that per assertion. Lists and objects are
/// compared structurally and are never turned into text, so assertions on large values do not serialize them.
/// </summary>
internal static class ValueComparison
{
    private const int PreviewLength = 200;
    private const int MaxCachedPatterns = 256;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<(string Pattern, bool CaseSensitive), Regex> Patterns = new();

    public static readonly string[] TypeNames = ["string", "number", "boolean", "array", "object", "null"];

    // ---- equality, ordering ----

    public static bool AreEqual(object? left, object? right, ComparisonOptions options)
    {
        left = Plain(left);
        right = Plain(right);

        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (IsContainer(left) || IsContainer(right))
        {
            return ContainersEqual(left, right);
        }

        if (options.Strict)
        {
            return (left, right) switch
            {
                _ when IsNumber(left) && IsNumber(right) => ToDecimal(left) == ToDecimal(right),
                (bool a, bool b) => a == b,
                (string a, string b) => string.Equals(a, b, options.Text),
                _ => false,
            };
        }

        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(TextOf(left), TextOf(right), options.Text);
    }

    public static int Compare(object? left, object? right, ComparisonOptions options)
    {
        left = Plain(left);
        right = Plain(right);

        if (IsContainer(left) || IsContainer(right))
        {
            throw new InvalidOperationException("Cannot order a list or object; compare a single value, or use an aggregation such as count.");
        }

        if (options.Strict && !((IsNumber(left) && IsNumber(right)) || (left is string && right is string)))
        {
            throw new InvalidOperationException("Strict comparison needs two numbers or two texts.");
        }

        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber)
            && (!options.Strict || (IsNumber(left) && IsNumber(right))))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(TextOf(left), TextOf(right), options.Text);
    }

    /// <summary>
    /// True when the two numbers differ by at most <see cref="ComparisonOptions.Tolerance"/>.
    /// </summary>
    public static bool IsApproximately(object? actual, object? expected, ComparisonOptions options)
    {
        if (!TryToDecimal(actual, out var actualNumber) || !TryToDecimal(expected, out var expectedNumber))
        {
            throw new InvalidOperationException("approx needs two numbers.");
        }

        return Math.Abs(actualNumber - expectedNumber) <= Math.Abs(options.Tolerance);
    }

    // ---- text ----

    /// <summary>
    /// Text values contain the text; lists contain an item that does; objects contain a key or value that does.
    /// The search walks the value in place instead of serializing it.
    /// </summary>
    public static bool ContainsText(object? actual, object? expected, ComparisonOptions options)
    {
        var needle = TextOf(Plain(expected)) ?? string.Empty;
        return Contains(Plain(actual), needle, options.Text);
    }

    public static bool StartsWith(object? actual, object? expected, ComparisonOptions options) =>
        TextFor("starts_with", actual)?.StartsWith(TextOf(Plain(expected)) ?? string.Empty, options.Text) == true;

    public static bool EndsWith(object? actual, object? expected, ComparisonOptions options) =>
        TextFor("ends_with", actual)?.EndsWith(TextOf(Plain(expected)) ?? string.Empty, options.Text) == true;

    /// <summary>
    /// Finds the regular expression anywhere in the text (anchor it with ^ and $ for a full match).
    /// </summary>
    public static bool Matches(object? actual, object? expected, ComparisonOptions options)
    {
        var text = TextFor("matches", actual);
        var pattern = TextOf(Plain(expected));
        if (string.IsNullOrEmpty(pattern))
        {
            throw new InvalidOperationException("matches needs a regular expression as the expected value.");
        }

        if (text is null)
        {
            return false;
        }

        try
        {
            return PatternFor(pattern, options.CaseSensitive).IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new InvalidOperationException($"The regular expression '{pattern}' took too long to evaluate.");
        }
    }

    // ---- membership, type, emptiness ----

    /// <summary>
    /// The expected value is a list (YAML list, JSON array text, or comma separated text); true when the actual value equals one of its items.
    /// </summary>
    public static bool IsOneOf(object? actual, object? expected, ComparisonOptions options) =>
        ExpectedItems(expected).Any(item => AreEqual(actual, item, options));

    public static bool IsNull(object? actual) => Plain(actual) is null;

    public static bool IsEmpty(object? actual) => Plain(actual) switch
    {
        null => true,
        string text => text.Length == 0,
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        IDictionary dictionary => dictionary.Count == 0,
        ICollection collection => collection.Count == 0,
        IEnumerable and not string => !((IEnumerable)Plain(actual)!).Cast<object?>().Any(),
        _ => false,
    };

    public static bool HasType(object? actual, object? expected, ComparisonOptions options)
    {
        var wanted = (TextOf(Plain(expected)) ?? string.Empty).Trim().ToLowerInvariant();
        if (!TypeNames.Contains(wanted))
        {
            throw new InvalidOperationException($"is_type expects one of: {string.Join(", ", TypeNames)}.");
        }

        return !options.IsMissing && TypeName(actual) == wanted;
    }

    private static string TypeName(object? value) => Plain(value) switch
    {
        null => "null",
        string => "string",
        bool => "boolean",
        var number when IsNumber(number) => "number",
        JsonArray => "array",
        JsonObject or IDictionary => "object",
        IEnumerable => "array",
        _ => "string",
    };

    // ---- helpers ----

    internal static bool TryToDecimal(object? value, out decimal result)
    {
        switch (Plain(value))
        {
            case decimal or int or long or double or float or short or byte or sbyte or ushort or uint or ulong:
                result = ToDecimal(Plain(value)!);
                return true;
            case string text:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// A short description of a value for messages. Never serializes a list or object in full.
    /// </summary>
    public static string Preview(object? value)
    {
        return Plain(value) switch
        {
            null => "null",
            string text => Truncate(text),
            JsonArray array => $"[list of {array.Count} items]",
            JsonObject obj => $"{{object with {obj.Count} properties}}",
            IDictionary dictionary => $"{{object with {dictionary.Count} properties}}",
            ICollection collection => $"[list of {collection.Count} items]",
            IEnumerable => "[list]",
            var other => Truncate(TextOf(other) ?? string.Empty),
        };
    }

    private static decimal ToDecimal(object number) => number switch
    {
        double d => (decimal)d,
        float f => (decimal)f,
        _ => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
    };

    private static bool IsNumber(object? value) =>
        value is decimal or int or long or double or float or short or byte or sbyte or ushort or uint or ulong;

    /// <summary>
    /// Reduces JSON wrappers to plain .NET values (string, number, bool, null); lists and objects are left as they are.
    /// </summary>
    private static object? Plain(object? value)
    {
        switch (value)
        {
            case LazyJson lazy:
                return Plain(lazy.Value);
            case JsonValue jsonValue:
                return Plain(jsonValue.GetValue<object?>());
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => element,
                };
            default:
                return value;
        }
    }

    private static bool IsContainer(object? value) =>
        value is JsonArray or JsonObject or JsonElement or IEnumerable and not string;

    private static bool ContainersEqual(object left, object right)
    {
        var leftNode = AsNode(left);
        var rightNode = AsNode(right);
        return leftNode is not null && rightNode is not null && JsonNode.DeepEquals(leftNode, rightNode);
    }

    /// <summary>
    /// Views a value as a JSON tree: JSON nodes as they are, JSON text parsed, other lists and objects converted.
    /// </summary>
    private static JsonNode? AsNode(object value)
    {
        try
        {
            return value switch
            {
                JsonNode node => node,
                string text => JsonNode.Parse(text),
                JsonElement element => JsonNode.Parse(element.GetRawText()),
                _ => JsonSerializer.SerializeToNode(value),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Contains(object? value, string needle, StringComparison comparison)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                return text.Contains(needle, comparison);
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Key.Contains(needle, comparison) || Contains(pair.Value, needle, comparison))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (Contains(array[i], needle, comparison))
                    {
                        return true;
                    }
                }

                return false;
            case IDictionary dictionary:
                return dictionary.Keys.Cast<object?>().Any(key => Contains(key, needle, comparison))
                    || dictionary.Values.Cast<object?>().Any(item => Contains(item, needle, comparison));
            case IEnumerable sequence:
                return sequence.Cast<object?>().Any(item => Contains(item, needle, comparison));
            default:
                var plain = Plain(value);
                return plain is not JsonNode && !ReferenceEquals(plain, value)
                    ? Contains(plain, needle, comparison)
                    : (TextOf(plain) ?? string.Empty).Contains(needle, comparison);
        }
    }

    /// <summary>
    /// The text of a single value; null when there is none, an error for lists and objects.
    /// </summary>
    private static string? TextFor(string operatorName, object? actual)
    {
        var plain = Plain(actual);
        if (plain is null)
        {
            return null;
        }

        return IsContainer(plain)
            ? throw new InvalidOperationException($"{operatorName} needs text, but the value is a list or object.")
            : TextOf(plain);
    }

    private static IEnumerable<object?> ExpectedItems(object? expected)
    {
        var plain = Plain(expected);
        switch (plain)
        {
            case null:
                return [];
            case JsonArray array:
                return array;
            case string text when text.TrimStart().StartsWith('['):
                try
                {
                    return JsonNode.Parse(text) is JsonArray parsed ? parsed : [text];
                }
                catch (JsonException)
                {
                    return [text];
                }

            case string text:
                return text.Split(',', StringSplitOptions.TrimEntries);
            case IEnumerable sequence:
                return sequence.Cast<object?>();
            default:
                return [plain];
        }
    }

    private static Regex PatternFor(string pattern, bool caseSensitive)
    {
        if (Patterns.Count >= MaxCachedPatterns)
        {
            Patterns.Clear();
        }

        return Patterns.GetOrAdd((pattern, caseSensitive), key =>
        {
            try
            {
                var flags = RegexOptions.CultureInvariant | (key.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                return new Regex(key.Pattern, flags, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid regular expression '{key.Pattern}': {ex.Message}");
            }
        });
    }

    private static string? TextOf(object? value) =>
        value is string text ? text : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static string Truncate(string text) =>
        text.Length > PreviewLength ? text[..PreviewLength] + "…" : text;
}
