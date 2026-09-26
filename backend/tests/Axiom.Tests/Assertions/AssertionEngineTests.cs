namespace Axiom.Tests.Assertions;

public class AssertionEngineTests
{
    private static readonly AssertionEngine Engine = Build.Engine();
    private static readonly Dictionary<string, object?> NoVariables = new(StringComparer.OrdinalIgnoreCase);

    private const string Doc = """
        {"s":"Hello","n":200,"ns":"200","f":1.5,"t":true,"nul":null,"arr":[1,2,3],"empty":[],"o":{},"e":"","mail":"Ann@Example.com",
         "items":[{"id":1,"name":"one","price":5.5,"tags":["a","b"]},{"id":2,"name":"two","price":9.5,"tags":["c"]}]}
        """;

    private static AssertionResult Eval(AssertionDefinition assertion, object? source = null)
    {
        source ??= JsonNode.Parse(Doc);
        return Engine.EvaluateAll(new StepDefinition { Assert = [assertion] }, NoVariables, _ => source)[0];
    }

    private static AssertionResult Eval(string path, string op, object? expected, bool strict = false, bool caseSensitive = false, decimal? tolerance = null, string? aggregate = null) =>
        Eval(Build.Assertion("body", op, expected, path, aggregate, strict, caseSensitive, tolerance));

    // ---- equality and ordering ----

    [Theory]
    [InlineData("n", "==", 200, true)]
    [InlineData("ns", "==", 200, true)]        // "200" equals 200 unless strict
    [InlineData("s", "==", "hello", true)]     // text ignores case by default
    [InlineData("n", "!=", 200, false)]
    [InlineData("n", ">", 100, true)]
    [InlineData("n", ">=", 200, true)]
    [InlineData("n", "<", 200, false)]
    [InlineData("n", "<=", 200, true)]
    [InlineData("n", ">", "100", true)]
    [InlineData("s", "<", "world", true)]
    [InlineData("f", "==", "1.5", true)]
    public void Comparisons_follow_the_loose_default_rules(string path, string op, object expected, bool passes) =>
        Assert.Equal(passes, Eval(path, op, expected).Passed);

    [Fact]
    public void Strict_disables_type_coercion()
    {
        Assert.False(Eval("ns", "==", 200, strict: true).Passed);
        Assert.True(Eval("n", "==", 200, strict: true).Passed);
        Assert.False(Eval("n", "==", "200", strict: true).Passed);
        Assert.True(Eval("ns", "!=", 200, strict: true).Passed);
        Assert.Equal(RunOutcome.Error, Eval("n", ">", "100", strict: true).Outcome); // cannot order a number against text
    }

    [Fact]
    public void Case_sensitive_applies_to_text_comparisons()
    {
        Assert.False(Eval("s", "==", "hello", caseSensitive: true).Passed);
        Assert.True(Eval("s", "==", "Hello", caseSensitive: true).Passed);
        Assert.False(Eval("s", "contains", "ELL", caseSensitive: true).Passed);
        Assert.True(Eval("s", "contains", "ELL").Passed);
        Assert.False(Eval("s", "starts_with", "he", caseSensitive: true).Passed);
        Assert.False(Eval("mail", "matches", "^ann@", caseSensitive: true).Passed);
        Assert.True(Eval("mail", "matches", "^ann@").Passed);
    }

    [Fact]
    public void Approx_uses_the_tolerance()
    {
        Assert.True(Eval("f", "approx", 1.6, tolerance: 0.1m).Passed);
        Assert.False(Eval("f", "approx", 1.8, tolerance: 0.1m).Passed);
        Assert.True(Eval("f", "approx", 1.5).Passed); // no tolerance means exact
        Assert.Equal(RunOutcome.Error, Eval("s", "approx", 1).Outcome);
    }

    // ---- text ----

    [Theory]
    [InlineData("s", "starts_with", "he", true)]
    [InlineData("s", "starts_with", "lo", false)]
    [InlineData("s", "ends_with", "LO", true)]
    [InlineData("n", "starts_with", "20", true)]
    [InlineData("mail", "matches", @"^[\w.]+@example\.com$", true)]
    [InlineData("s", "matches", @"^\d+$", false)]
    [InlineData("s", "contains", "ell", true)]
    [InlineData("s", "not_contains", "zzz", true)]
    public void Text_operators(string path, string op, string expected, bool passes) =>
        Assert.Equal(passes, Eval(path, op, expected).Passed);

    [Fact]
    public void Contains_looks_inside_lists_and_objects_without_matching_across_items()
    {
        Assert.True(Eval("arr", "contains", 2).Passed);
        Assert.False(Eval("arr", "contains", 12).Passed);                  // "[1,2,3]" as text would contain "1,2"; items must not be glued together
        Assert.True(Eval("items", "contains", "two").Passed);              // a value inside an object inside a list
        Assert.True(Eval("items.0", "contains", "price").Passed);          // an object matches on its keys too
        Assert.False(Eval("items", "contains", "three").Passed);
    }

    [Fact]
    public void Whole_body_contains_searches_the_raw_text_and_does_not_parse_it()
    {
        var body = new LazyJson("{\"error\":\"bad request\"}");
        Assert.True(Eval(Build.Assertion("body", "contains", "bad request"), body).Passed);
        Assert.True(Eval(Build.Assertion("body", "contains", "\"error\""), body).Passed); // raw text, quotes included
        Assert.True(Eval(Build.Assertion("body", "not_contains", "success"), body).Passed);
    }

    [Fact]
    public void Invalid_regular_expressions_are_errors_not_failures()
    {
        var result = Eval("s", "matches", "(");
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("Invalid regular expression", result.Error);
    }

    // ---- structural equality ----

    [Fact]
    public void Lists_and_objects_compare_structurally()
    {
        Assert.True(Eval("arr", "==", "[1, 2, 3]").Passed);                 // JSON text as the expected value
        Assert.True(Eval("arr", "==", new List<object?> { 1, 2, 3 }).Passed); // a YAML list
        Assert.False(Eval("arr", "==", "[1,2]").Passed);
        Assert.False(Eval("arr", "==", "not json").Passed);
        Assert.True(Eval("o", "==", "{}").Passed);
    }

    [Fact]
    public void Ordering_a_list_is_an_error()
    {
        var result = Eval("arr", ">", 1);
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("Cannot order", result.Error);
    }

    // ---- membership ----

    [Theory]
    [InlineData("n", "in", "200, 201, 204", true)]
    [InlineData("n", "in", "301,302", false)]
    [InlineData("n", "not_in", "301,302", true)]
    [InlineData("s", "in", "[\"hello\",\"bye\"]", true)]
    public void In_accepts_csv_and_json_text(string path, string op, string expected, bool passes) =>
        Assert.Equal(passes, Eval(path, op, expected).Passed);

    [Fact]
    public void In_accepts_a_list_and_respects_strict()
    {
        Assert.True(Eval("n", "in", new List<object?> { 200, 201 }).Passed);
        Assert.False(Eval("ns", "in", new List<object?> { 200 }, strict: true).Passed);
    }

    // ---- presence, null, missing, emptiness, type ----

    [Fact]
    public void Exists_means_a_non_null_value_and_is_null_and_is_missing_tell_null_and_absent_apart()
    {
        Assert.True(Eval("s", "exists", null).Passed);
        Assert.False(Eval("nul", "exists", null).Passed);
        Assert.False(Eval("zzz", "exists", null).Passed);
        Assert.True(Eval("zzz", "not_exists", null).Passed);

        Assert.True(Eval("nul", "is_null", null).Passed);
        Assert.False(Eval("zzz", "is_null", null).Passed);
        Assert.False(Eval("s", "is_null", null).Passed);

        Assert.True(Eval("zzz", "is_missing", null).Passed);
        Assert.False(Eval("nul", "is_missing", null).Passed);
        Assert.True(Eval("o.x.y", "is_missing", null).Passed);
        Assert.True(Eval("arr.9", "is_missing", null).Passed);
    }

    [Theory]
    [InlineData("e", true)]
    [InlineData("empty", true)]
    [InlineData("o", true)]
    [InlineData("nul", true)]
    [InlineData("zzz", true)]
    [InlineData("s", false)]
    [InlineData("arr", false)]
    [InlineData("n", false)]
    public void Is_empty(string path, bool empty)
    {
        Assert.Equal(empty, Eval(path, "is_empty", null).Passed);
        Assert.Equal(!empty, Eval(path, "is_not_empty", null).Passed);
    }

    [Theory]
    [InlineData("s", "string", true)]
    [InlineData("n", "number", true)]
    [InlineData("ns", "number", false)]     // the text "200" is a string, not a number
    [InlineData("ns", "string", true)]
    [InlineData("t", "boolean", true)]
    [InlineData("arr", "array", true)]
    [InlineData("o", "object", true)]
    [InlineData("nul", "null", true)]
    [InlineData("zzz", "null", false)]      // a missing value is not a null value
    public void Is_type(string path, string type, bool passes) =>
        Assert.Equal(passes, Eval(path, "is_type", type).Passed);

    [Fact]
    public void Is_type_rejects_unknown_type_names()
    {
        var result = Eval("s", "is_type", "banana");
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("is_type expects", result.Error);
    }

    // ---- aggregations and wildcard paths ----

    [Fact]
    public void Count_sum_avg_min_max()
    {
        Assert.True(Eval("arr", "==", 3, aggregate: "count").Passed);
        Assert.True(Eval("o", "==", 0, aggregate: "count").Passed);
        Assert.True(Eval("items.*.price", "==", 15, aggregate: "sum").Passed);
        Assert.True(Eval("items.*.price", "==", 7.5, aggregate: "avg").Passed);
        Assert.True(Eval("items.*.price", "==", 5.5, aggregate: "min").Passed);
        Assert.True(Eval("items.*.price", "==", 9.5, aggregate: "max").Passed);
        Assert.True(Eval("items.*.tags.*", "==", 3, aggregate: "count").Passed);   // nested wildcards flatten
        Assert.True(Eval("items", "==", 2, aggregate: "COUNT").Passed);            // names are case-insensitive
    }

    [Fact]
    public void Wildcards_skip_items_where_the_rest_of_the_path_is_missing()
    {
        var doc = JsonNode.Parse("{\"items\":[{\"opt\":1},{\"x\":2},{\"opt\":3}]}");
        Assert.True(Eval(Build.Assertion("b", "==", 2, "items.*.opt", "count"), doc).Passed);
        Assert.True(Eval(Build.Assertion("b", "==", 4, "items.*.opt", "sum"), doc).Passed);
    }

    [Fact]
    public void Aggregation_problems_are_errors()
    {
        Assert.Equal(RunOutcome.Error, Eval("zzz", "==", 0, aggregate: "count").Outcome);              // missing value
        Assert.Equal(RunOutcome.Error, Eval("items.*.name", "==", 0, aggregate: "sum").Outcome);       // not numbers
        Assert.Equal(RunOutcome.Error, Eval("items.*.nothing", "==", 0, aggregate: "avg").Outcome);    // empty list
        Assert.Equal(RunOutcome.Error, Eval("s", "==", 0, aggregate: "count").Outcome);                // text is not a list
        Assert.Equal(RunOutcome.Error, Eval("arr", "==", 1, aggregate: "median").Outcome);             // unknown aggregation
        Assert.True(Eval("items.*.nothing", "==", 0, aggregate: "sum").Passed);                         // sum of nothing is 0
    }

    // ---- outcomes ----

    [Fact]
    public void A_mismatch_is_a_failure_with_expected_and_actual()
    {
        var result = Eval("s", "==", "wrong");
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.False(result.Passed);
        Assert.Equal("wrong", result.Expected);
        Assert.Equal("Hello", result.Actual?.ToString());
        Assert.StartsWith("Expected '==' with value 'wrong', actual 'Hello'", result.Error);
    }

    [Fact]
    public void A_failure_on_a_missing_path_says_so()
    {
        var result = Eval("nothing", "==", "x");
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Contains("'body.nothing' was not found", result.Error);
    }

    [Fact]
    public void Things_that_cannot_be_evaluated_are_errors_not_failures()
    {
        Assert.Equal(RunOutcome.Error, Eval("s", "equals", "x").Outcome);                                       // unknown operator
        Assert.Equal(RunOutcome.Error, Eval("s", "==", "{{not_defined}}").Outcome);                              // unresolved variable
        var unknownSource = Engine.EvaluateAll(new StepDefinition { Assert = [Build.Assertion("bodyy", "==", 1)] }, NoVariables, _ => null)[0];
        Assert.Equal(RunOutcome.Error, unknownSource.Outcome);
        Assert.Contains("Unknown source 'bodyy'", unknownSource.Error);
    }

    [Fact]
    public void Presence_operators_still_work_on_an_unknown_source()
    {
        var result = Engine.EvaluateAll(new StepDefinition { Assert = [Build.Assertion("nope", "is_missing")] }, NoVariables, _ => null)[0];
        Assert.Equal(RunOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void Expected_values_are_resolved_from_variables()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["wanted"] = "Hello" };
        var result = Engine.EvaluateAll(
            new StepDefinition { Assert = [Build.Assertion("body", "==", "{{wanted}}", "s")] },
            variables,
            _ => JsonNode.Parse(Doc))[0];
        Assert.True(result.Passed);
        Assert.Equal("Hello", result.Expected);
    }

    [Fact]
    public void An_error_in_one_assertion_does_not_stop_the_others()
    {
        var doc = JsonNode.Parse(Doc);
        var results = Engine.EvaluateAll(
            new StepDefinition { Assert = [Build.Assertion("b", "nope", 1, "s"), Build.Assertion("b", "==", 200, "n")] },
            NoVariables,
            _ => doc);
        Assert.Equal([RunOutcome.Error, RunOutcome.Passed], results.Select(r => r.Outcome));
    }

    [Fact]
    public void Large_values_are_kept_complete_in_the_result_but_previewed_in_the_message()
    {
        var big = new string('x', 5000);
        var result = Eval(Build.Assertion("body", "==", "y", "text"), JsonNode.Parse($"{{\"text\":\"{big}\"}}"));
        Assert.Equal(RunOutcome.Failed, result.Outcome);
        Assert.Equal(big, result.Actual?.ToString());                   // nothing is truncated in the result
        Assert.True(result.Error!.Length < 400);                        // the message only carries a preview
    }

    [Fact]
    public void Results_carry_source_path_and_aggregate_for_display()
    {
        var result = Eval("items.*.price", "==", 15, aggregate: "sum");
        Assert.Equal("body", result.Source);
        Assert.Equal("items.*.price", result.Path);
        Assert.Equal("sum", result.Aggregate);
        Assert.Equal("==", result.Operator);
    }

    [Fact]
    public void Custom_operators_and_aggregations_can_be_registered()
    {
        var engine = new AssertionEngine(
            [.. BuiltInAssertionOperators.All, new IsEvenOperator()],
            [.. BuiltInAssertionAggregations.All]);
        var result = engine.EvaluateAll(new StepDefinition { Assert = [Build.Assertion("n", "is_even")] }, NoVariables, _ => 4)[0];
        Assert.True(result.Passed);
        Assert.Contains("is_even", engine.OperatorNames);
    }

    private sealed class IsEvenOperator : IAssertionOperator
    {
        public string Name => "is_even";

        public bool Evaluate(object? actual, object? expected, ComparisonOptions options) => Convert.ToInt32(actual) % 2 == 0;
    }
}
