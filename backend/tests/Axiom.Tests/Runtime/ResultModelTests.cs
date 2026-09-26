using System.Text.Json;

namespace Axiom.Tests.Runtime;

public class ResultModelTests
{
    private static AssertionResult Assertion(RunOutcome outcome) => new() { Source = "s", Operator = "==", Expected = 1, Actual = 1, Outcome = outcome };

    private static StepExecutionResult Step(bool passed, string? error = null, IReadOnlyList<AssertionResult>? assertions = null, IReadOnlyList<StepExecutionResult>? children = null) =>
        new() { Id = "s", Type = "request", Name = "s", Passed = passed, Error = error, Assertions = assertions ?? [], Children = children };

    private static TestCaseExecutionResult Test(params StepExecutionResult[] steps) =>
        new() { Name = "t", SourceFile = "t.test.yaml", Steps = steps };

    [Fact]
    public void An_assertion_is_passed_only_when_its_outcome_is_passed()
    {
        Assert.True(Assertion(RunOutcome.Passed).Passed);
        Assert.False(Assertion(RunOutcome.Failed).Passed);
        Assert.False(Assertion(RunOutcome.Error).Passed);
    }

    [Fact]
    public void A_step_is_an_error_when_it_has_an_error_an_errored_assertion_or_an_errored_child()
    {
        Assert.Equal(RunOutcome.Passed, Step(true).Outcome);
        Assert.Equal(RunOutcome.Failed, Step(false, assertions: [Assertion(RunOutcome.Failed)]).Outcome);
        Assert.Equal(RunOutcome.Error, Step(false, error: "boom").Outcome);
        Assert.Equal(RunOutcome.Error, Step(false, assertions: [Assertion(RunOutcome.Failed), Assertion(RunOutcome.Error)]).Outcome);
        Assert.Equal(RunOutcome.Error, Step(false, children: [Step(false, error: "inner")]).Outcome);
        Assert.Equal(RunOutcome.Failed, Step(false, children: [Step(false, assertions: [Assertion(RunOutcome.Failed)])]).Outcome);
    }

    [Fact]
    public void A_test_is_an_error_if_any_step_is_otherwise_failed_or_passed()
    {
        Assert.Equal(RunOutcome.Passed, Test(Step(true), Step(true)).Outcome);
        Assert.Equal(RunOutcome.Passed, Test().Outcome);                                   // no steps, nothing failed
        Assert.Equal(RunOutcome.Failed, Test(Step(true), Step(false)).Outcome);
        Assert.Equal(RunOutcome.Error, Test(Step(false), Step(false, error: "x")).Outcome);
        Assert.False(Test(Step(false)).Passed);
    }

    [Fact]
    public void A_collection_result_counts_each_outcome_and_the_unsuccessful_total()
    {
        var result = new CollectionExecutionResult
        {
            CollectionName = "c", RootPath = "/c",
            TestCases = [Test(Step(true)), Test(Step(false)), Test(Step(false)), Test(Step(false, error: "x"))],
        };

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(1, result.PassedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(3, result.UnsuccessfulCount);
        Assert.Equal(25.0, result.SuccessRate);
    }

    [Fact]
    public void Outcomes_are_written_as_words_in_json()
    {
        var json = JsonSerializer.Serialize(Test(Step(false, error: "x", assertions: [Assertion(RunOutcome.Failed)])), JsonSerializerOptions.Web);

        Assert.Contains("\"outcome\":\"Error\"", json);      // the step
        Assert.Contains("\"outcome\":\"Failed\"", json);     // the assertion
        Assert.Contains("\"passed\":false", json);
    }
}
