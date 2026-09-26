using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Runtime;

/// <summary>
/// Loose comparison rules shared by assertion operators: numbers compare numerically, everything else as
/// case-insensitive text. Lists and objects are compared structurally and are never turned into text, so
/// assertions on large values do not serialize them.
/// </summary>
internal static class ValueComparison
{
    private const int PreviewLength = 200;

    public static bool AreEqual(object? left, object? right)
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

        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(TextOf(left), TextOf(right), StringComparison.OrdinalIgnoreCase);
    }

    public static int Compare(object? left, object? right)
    {
        left = Plain(left);
        right = Plain(right);

        if (IsContainer(left) || IsContainer(right))
        {
            throw new InvalidOperationException("Cannot order a list or object; compare a single value, or use an aggregation such as count.");
        }

        if (TryToDecimal(left, out var leftNumber) && TryToDecimal(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(TextOf(left), TextOf(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Text values contain the text; lists contain an item that does; objects contain a key or value that does.
    /// The search walks the value in place instead of serializing it.
    /// </summary>
    public static bool ContainsText(object? actual, object? expected)
    {
        var needle = TextOf(Plain(expected)) ?? string.Empty;
        return Contains(Plain(actual), needle);
    }

    internal static bool TryToDecimal(object? value, out decimal result)
    {
        switch (Plain(value))
        {
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
            case string text:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>A short description of a value for messages. Never serializes a list or object in full.</summary>
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

    /// <summary>Reduces JSON wrappers to plain .NET values (string, number, bool, null); lists and objects are left as they are.</summary>
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

    /// <summary>Views a value as a JSON tree: JSON nodes as they are, JSON text parsed, other lists and objects converted.</summary>
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

    private static bool Contains(object? value, string needle)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                return text.Contains(needle, StringComparison.OrdinalIgnoreCase);
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Key.Contains(needle, StringComparison.OrdinalIgnoreCase) || Contains(pair.Value, needle))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (Contains(array[i], needle))
                    {
                        return true;
                    }
                }

                return false;
            case IDictionary dictionary:
                return dictionary.Keys.Cast<object?>().Any(key => Contains(key, needle))
                    || dictionary.Values.Cast<object?>().Any(item => Contains(item, needle));
            case IEnumerable sequence:
                return sequence.Cast<object?>().Any(item => Contains(item, needle));
            default:
                var plain = Plain(value);
                return plain is not JsonNode && !ReferenceEquals(plain, value)
                    ? Contains(plain, needle)
                    : (TextOf(plain) ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string? TextOf(object? value) =>
        value is string text ? text : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static string Truncate(string text) =>
        text.Length > PreviewLength ? text[..PreviewLength] + "…" : text;
}
