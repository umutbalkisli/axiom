namespace Axiom.Tests.Assertions;

public class TemplateResolverTests
{
    private static Dictionary<string, object?> Vars(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ResolveString_replaces_every_token_and_trims_spaces()
    {
        var text = TemplateResolver.ResolveString("{{ base }}/todos/{{id}}?x={{base}}", Vars(("base", "http://h"), ("id", 7)));
        Assert.Equal("http://h/todos/7?x=http://h", text);
    }

    [Fact]
    public void ResolveString_leaves_text_without_tokens_alone_and_handles_empty()
    {
        Assert.Equal("plain", TemplateResolver.ResolveString("plain", Vars()));
        Assert.Equal("", TemplateResolver.ResolveString("", Vars()));
    }

    [Fact]
    public void ResolveString_throws_for_an_unknown_variable()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TemplateResolver.ResolveString("{{nope}}", Vars()));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Variable_names_are_case_insensitive()
    {
        Assert.Equal("v", TemplateResolver.ResolveString("{{NAME}}", Vars(("name", "v"))));
    }

    [Fact]
    public void ResolveObject_walks_json_lists_dictionaries_and_rows()
    {
        var rows = new List<Dictionary<string, object?>> { new(StringComparer.OrdinalIgnoreCase) { ["Id"] = 42 } };
        var vars = Vars(("todo", rows), ("resp", new LazyJson("{\"a\":{\"b\":[10,20]}}")), ("dict", new Dictionary<string, object?> { ["k"] = "v" }));

        Assert.Equal(42, TemplateResolver.ResolveObject("todo.0.Id", vars));
        Assert.Equal("20", TemplateResolver.ResolveObject("resp.a.b.1", vars)?.ToString());
        Assert.Equal("v", TemplateResolver.ResolveObject("dict.k", vars));
        Assert.Null(TemplateResolver.ResolveObject("todo.5.Id", vars));
        Assert.Null(TemplateResolver.ResolveObject("missing.x", vars));
    }

    [Fact]
    public void TryResolveFrom_tells_a_missing_path_from_a_null_value()
    {
        var doc = JsonNode.Parse("{\"present\":null,\"list\":[1]}");

        Assert.True(TemplateResolver.TryResolveFrom(doc, "present", out var value));
        Assert.Null(value);
        Assert.False(TemplateResolver.TryResolveFrom(doc, "absent", out _));
        Assert.False(TemplateResolver.TryResolveFrom(doc, "list.3", out _));
        Assert.False(TemplateResolver.TryResolveFrom(doc, "present.deeper", out _));
        Assert.False(TemplateResolver.TryResolveFrom(null, null, out _));
    }

    [Fact]
    public void Wildcards_collect_values_and_report_found_even_for_empty_lists()
    {
        var doc = JsonNode.Parse("{\"items\":[{\"p\":1},{\"p\":2}],\"none\":[]}");

        Assert.True(TemplateResolver.TryResolveFrom(doc, "items.*.p", out var values));
        Assert.Equal(2, ((List<object?>)values!).Count);
        Assert.True(TemplateResolver.TryResolveFrom(doc, "none.*", out var empty));
        Assert.Empty((List<object?>)empty!);
        Assert.False(TemplateResolver.TryResolveFrom(doc, "missing.*", out _));
    }

    [Fact]
    public void A_saved_response_exposes_its_body_directly_and_the_http_response_under_at_http()
    {
        var response = new LazyJson("{\"status\":\"ok\",\"headers\":{\"note\":\"body\"}}", new Dictionary<string, object?>
        {
            ["status"] = 201,
            ["headers"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["X-Id"] = "abc" },
        });
        var vars = Vars(("resp", response));

        Assert.Equal("ok", TemplateResolver.ResolveObject("resp.status", vars)?.ToString());     // a body field, never the HTTP status
        Assert.Equal("body", TemplateResolver.ResolveObject("resp.headers.note", vars)?.ToString()); // body field named headers
        Assert.Equal(201, TemplateResolver.ResolveObject("resp.@http.status", vars));
        Assert.Equal("abc", TemplateResolver.ResolveObject("resp.@http.headers.x-id", vars));
        Assert.Equal("abc", TemplateResolver.ResolveObject("resp.@HTTP.headers.X-ID", vars));
        Assert.Contains("\"status\"", (string)TemplateResolver.ResolveObject("resp.@http.body_text", vars)!);
        Assert.Null(TemplateResolver.ResolveObject("resp.@http.nothing", vars));
    }

    [Fact]
    public void A_saved_response_with_a_non_json_body_resolves_to_its_text()
    {
        var vars = Vars(("plain", new LazyJson("hello world")));
        Assert.Equal("hello world", TemplateResolver.ResolveString("{{plain}}", vars));
    }
}
