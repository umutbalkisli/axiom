namespace Axiom.Tests.Assertions;

public class LazyJsonTests
{
    [Fact]
    public void Json_text_is_parsed_on_first_use()
    {
        var body = new LazyJson("{\"a\":1}");
        Assert.NotNull(body.Node);
        Assert.IsAssignableFrom<JsonNode>(body.Value);
        Assert.Equal("{\"a\":1}", body.Text);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("")]
    [InlineData("{not json")]
    public void Text_that_is_not_json_has_no_node_and_its_value_is_the_text(string text)
    {
        var body = new LazyJson(text);
        Assert.Null(body.Node);
        Assert.Equal(text, body.Value);
    }

    [Fact]
    public void Http_members_include_the_body_text_next_to_the_given_members()
    {
        var body = new LazyJson("{}", new Dictionary<string, object?> { ["status"] = 200 });
        Assert.Equal(200, body.Http["status"]);
        Assert.Equal("{}", body.Http["body_text"]);
        Assert.Equal("{}", body.Http["BODY_TEXT"]);   // case-insensitive
    }

    [Fact]
    public void Clone_gives_an_independent_copy_with_the_same_content()
    {
        var original = new LazyJson("{\"a\":1}", new Dictionary<string, object?> { ["status"] = 200 });
        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Text, clone.Text);
        Assert.NotSame(original.Node, clone.Node);       // parallel tests must not share one JSON tree
        Assert.Equal(200, clone.Http["status"]);
    }
}
