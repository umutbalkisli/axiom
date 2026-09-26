using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiom.Runtime;

/// <summary>
/// A response body that is only parsed as JSON when something reads it. Most assertions look at the status or
/// timing, so large responses are never turned into a JSON tree unless a test actually uses the body.
/// </summary>
public sealed class LazyJson(string text, IReadOnlyDictionary<string, object?>? members = null)
{
    private readonly Lazy<JsonNode?> _node = new(() => Parse(text), LazyThreadSafetyMode.ExecutionAndPublication);

    public string Text => text;

    /// <summary>The name that opens the response-level members of a saved result: <c>my_response.@http.headers</c>.</summary>
    public const string HttpMemberName = "@http";

    private readonly Lazy<IReadOnlyDictionary<string, object?>> _http = new(() =>
    {
        var http = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in members ?? new Dictionary<string, object?>())
        {
            http[name] = value;
        }

        http["body_text"] = text;
        return http;
    });

    /// <summary>
    /// The response itself (status, headers, duration_ms, body_text), reached as <c>@http</c>. The <c>@</c> keeps it apart
    /// from the fields of the body, so a body field can never be mistaken for it.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Http => _http.Value;

    /// <summary>The parsed JSON tree, or null when the body is not JSON.</summary>
    public JsonNode? Node => _node.Value;

    /// <summary>The JSON tree when the body is JSON, otherwise the raw text.</summary>
    public object Value => (object?)Node ?? text;

    /// <summary>An independent copy, so parallel tests never share (and race on) one JSON tree.</summary>
    public LazyJson Clone() => new(text, members);

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
