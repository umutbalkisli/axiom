using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiom.Runtime;

/// <summary>
/// Resolves <c>{{variable}}</c> templates and dotted paths against the values of a run.
/// </summary>
public static partial class TemplateResolver
{
    private static readonly Regex TokenRegex = TokenPattern();

    /// <summary>
    /// Replaces every <c>{{expression}}</c> in <paramref name="value"/>; throws when a variable does not exist.
    /// </summary>
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

            // Invariant: a number must read the same on every machine (1.5, never 1,5 on a Turkish system).
            return Convert.ToString(resolved, CultureInfo.InvariantCulture) ?? string.Empty;
        });
    }

    /// <summary>
    /// Evaluates a dotted expression (<c>todo.0.Id</c>) against <paramref name="context"/>; null when it does not resolve.
    /// </summary>
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

    /// <summary>
    /// Applies <paramref name="path"/> to <paramref name="source"/>; null when it does not resolve.
    /// </summary>
    public static object? ResolveFrom(object? source, string? path)
    {
        TryResolveFrom(source, path, out var value);
        return value;
    }

    /// <summary>
    /// Resolves <paramref name="path"/> inside <paramref name="source"/>. Returns false when the source is unknown or a
    /// step of the path does not exist; a value that is present but JSON null is found, with a null value.
    /// </summary>
    public static bool TryResolveFrom(object? source, string? path, out object? value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            value = UnwrapJson(source);
            return source is not null;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var found = TryResolveSegments(source, segments, 0, out var resolved);
        value = UnwrapJson(resolved);
        return found;
    }

    /// <summary>
    /// Walks the path. A <c>*</c> segment fans out over every element of a list (or property of an object) and
    /// collects what the rest of the path finds in each; elements where it finds nothing are skipped.
    /// </summary>
    private static bool TryResolveSegments(object? current, string[] segments, int start, out object? value)
    {
        for (var i = start; i < segments.Length; i++)
        {
            if (segments[i] != "*")
            {
                if (!TryResolveSegment(current, segments[i], out current))
                {
                    value = null;
                    return false;
                }

                continue;
            }

            current = Unlazy(current);
            if (current is null || !TryEnumerateChildren(current, out var children))
            {
                value = null;
                return false;
            }

            var restHasWildcard = Array.IndexOf(segments, "*", i + 1) >= 0;
            var results = new List<object?>();
            foreach (var child in children)
            {
                if (!TryResolveSegments(child, segments, i + 1, out var childValue))
                {
                    continue;
                }

                var unwrapped = UnwrapJson(childValue);
                if (restHasWildcard && unwrapped is List<object?> nested)
                {
                    results.AddRange(nested);
                }
                else if (unwrapped is not null)
                {
                    results.Add(unwrapped);
                }
            }

            value = results;
            return true;
        }

        value = current;
        return true;
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

    private static object? ResolveSegment(object? current, string segment) =>
        TryResolveSegment(current, segment, out var value) ? value : null;

    /// <summary>
    /// Reads one path segment. Returns false when it does not exist; a present JSON null returns true with a null value.
    /// </summary>
    private static bool TryResolveSegment(object? current, string segment, out object? value)
    {
        value = null;
        switch (current)
        {
            case null:
                return false;
            case LazyJson response:
                // A saved response: "@http" opens the response itself (status, headers, ...); anything else is a field of the body.
                if (segment.Equals(LazyJson.HttpMemberName, StringComparison.OrdinalIgnoreCase))
                {
                    value = response.Http;
                    return true;
                }

                return TryResolveSegment(response.Value, segment, out value);
            case System.Collections.IList list when int.TryParse(segment, out var listIndex):
                return TryIndex(listIndex, list.Count, i => list[i], out value);
            case JsonObject jsonObject:
                var present = jsonObject.TryGetPropertyValue(segment, out var node);
                value = node;
                return present;
            case JsonArray jsonArray when int.TryParse(segment, out var arrayIndex):
                return TryIndex(arrayIndex, jsonArray.Count, i => jsonArray[i], out value);
            case IDictionary<string, object?> dictObject:
                return dictObject.TryGetValue(segment, out value);
            case IDictionary<string, string> dictString:
                var hasText = dictString.TryGetValue(segment, out var text);
                value = text;
                return hasText;
            case IDictionary<object, object> dictYaml:
                foreach (var pair in dictYaml)
                {
                    if (string.Equals(pair.Key?.ToString(), segment, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }

                return false;
        }

        var property = current.GetType().GetProperty(segment);
        if (property is null)
        {
            return false;
        }

        value = property.GetValue(current);
        return true;
    }

    private static bool TryIndex(int index, int count, Func<int, object?> get, out object? value)
    {
        if (index >= 0 && index < count)
        {
            value = get(index);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// A lazily parsed response body becomes its JSON tree (or its text when it is not JSON).
    /// </summary>
    private static object? Unlazy(object? value) => value is LazyJson lazy ? lazy.Value : value;

    internal static object? UnwrapJson(object? value)
    {
        return value switch
        {
            LazyJson lazy => UnwrapJson(lazy.Value),
            JsonValue jsonValue => jsonValue.GetValue<object?>(),
            JsonNode jsonNode => jsonNode,
            _ => value,
        };
    }

    [GeneratedRegex("\\{\\{(.*?)\\}\\}")]
    private static partial Regex TokenPattern();
}