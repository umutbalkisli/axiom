using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiom.Runtime;

public static partial class TemplateResolver
{
    private static readonly Regex TokenRegex = TokenPattern();

    public static string ResolveString(string value, IReadOnlyDictionary<string, object?> context)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return TokenRegex.Replace(value, match =>
        {
            var expr = match.Groups[1].Value.Trim();
            var resolved = ResolveObject(expr, context);
            if (resolved is null)
            {
                throw new InvalidOperationException($"Template variable '{expr}' was not found in the execution context.");
            }

            return resolved.ToString() ?? string.Empty;
        });
    }

    public static object? ResolveObject(string expression, IReadOnlyDictionary<string, object?> context)
    {
        var segments = expression.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (!context.TryGetValue(segments[0], out var current))
        {
            return null;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            current = ResolveSegment(current, segments[i]);
            if (current is null)
            {
                return null;
            }
        }

        return UnwrapJson(current);
    }

    public static object? ResolveFrom(object? source, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return UnwrapJson(source);
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return UnwrapJson(ResolveSegments(source, segments, 0));
    }

    /// <summary>
    /// Walks the path. A <c>*</c> segment fans out over every element of a list (or property of an object) and
    /// collects what the rest of the path finds in each; elements where it finds nothing are skipped.
    /// </summary>
    private static object? ResolveSegments(object? current, string[] segments, int start)
    {
        for (var i = start; i < segments.Length; i++)
        {
            if (segments[i] != "*")
            {
                current = ResolveSegment(current, segments[i]);
                if (current is null)
                {
                    return null;
                }

                continue;
            }

            if (current is null || !TryEnumerateChildren(current, out var children))
            {
                return null;
            }

            var restHasWildcard = Array.IndexOf(segments, "*", i + 1) >= 0;
            var results = new List<object?>();
            foreach (var child in children)
            {
                var value = UnwrapJson(ResolveSegments(child, segments, i + 1));
                if (restHasWildcard && value is List<object?> nested)
                {
                    results.AddRange(nested);
                }
                else if (value is not null)
                {
                    results.Add(value);
                }
            }

            return results;
        }

        return current;
    }

    private static bool TryEnumerateChildren(object value, out IEnumerable<object?> children)
    {
        switch (value)
        {
            case JsonArray array:
                children = array;
                return true;
            case JsonObject obj:
                children = obj.Select(pair => (object?)pair.Value);
                return true;
            case IDictionary<string, object?> dictionary:
                children = dictionary.Values;
                return true;
            case string:
                break;
            case System.Collections.IEnumerable sequence:
                children = sequence.Cast<object?>();
                return true;
        }

        children = [];
        return false;
    }

    private static object? ResolveSegment(object? current, string segment)
    {
        if (current is null)
        {
            return null;
        }

        if (TryResolveList(current, segment, out var listValue)) return listValue;
        if (TryResolveJson(current, segment, out var jsonValue)) return jsonValue;
        if (TryResolveDictionary(current, segment, out var dictValue)) return dictValue;
        return TryResolveProperty(current, segment, out var propertyValue) ? propertyValue : null;
    }

    private static bool TryResolveList(object current, string segment, out object? value)
    {
        if (current is System.Collections.IList list && int.TryParse(segment, out var index))
        {
            value = index >= 0 && index < list.Count ? list[index] : null;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryResolveJson(object current, string segment, out object? value)
    {
        if (current is JsonObject jsonObject)
        {
            value = jsonObject.TryGetPropertyValue(segment, out var node) ? node : null;
            return true;
        }

        if (current is JsonArray jsonArray && int.TryParse(segment, out var index))
        {
            value = index >= 0 && index < jsonArray.Count ? jsonArray[index] : null;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryResolveDictionary(object current, string segment, out object? value)
    {
        if (current is IDictionary<string, object?> dictObject)
        {
            return dictObject.TryGetValue(segment, out value);
        }

        if (current is IDictionary<string, string> dictString)
        {
            var ok = dictString.TryGetValue(segment, out var textValue);
            value = textValue;
            return ok;
        }

        if (current is IDictionary<object, object> dictYaml)
        {
            foreach (var pair in dictYaml)
            {
                if (string.Equals(pair.Key?.ToString(), segment, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static bool TryResolveProperty(object current, string segment, out object? value)
    {
        var property = current.GetType().GetProperty(segment);
        if (property is null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(current);
        return true;
    }

    internal static object? UnwrapJson(object? value)
    {
        return value switch
        {
            JsonValue jsonValue => jsonValue.GetValue<object?>(),
            JsonNode jsonNode => jsonNode,
            _ => value,
        };
    }

    [GeneratedRegex("\\{\\{(.*?)\\}\\}")]
    private static partial Regex TokenPattern();
}