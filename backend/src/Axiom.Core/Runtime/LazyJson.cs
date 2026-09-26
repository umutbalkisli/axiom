using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Runtime;

/// <summary>
/// A response body that is only parsed as JSON when something reads it. Most assertions look at the status or
/// timing, so large responses are never turned into a JSON tree unless a test actually uses the body.
/// </summary>
public sealed class LazyJson(string text)
{
    private readonly Lazy<JsonNode?> _node = new(() => Parse(text), LazyThreadSafetyMode.ExecutionAndPublication);

    public string Text => text;

    /// <summary>The parsed JSON tree, or null when the body is not JSON.</summary>
    public JsonNode? Node => _node.Value;

    /// <summary>The JSON tree when the body is JSON, otherwise the raw text.</summary>
    public object Value => (object?)Node ?? text;

    /// <summary>An independent copy, so parallel tests never share (and race on) one JSON tree.</summary>
    public LazyJson Clone() => new(text);

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
