using Axiom.Services;

namespace Axiom.Tests.Host;

public class ReportFormatterTests
{
    private static AssertionResult Assertion(RunOutcome outcome, string? error = null, string? path = null, string? aggregate = null) => new()
    {
        Source = "body", Path = path, Aggregate = aggregate, Operator = "==", Expected = 1, Actual = 2, Outcome = outcome, Error = error,
    };

    private static TestCaseExecutionResult Test(string name, params StepExecutionResult[] steps) => new()
    {
        Name = name, SourceFile = name + ".test.yaml", Steps = steps, StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
    };

    private static StepExecutionResult Step(string id, bool passed, string? error = null, IReadOnlyList<AssertionResult>? assertions = null, IReadOnlyList<StepExecutionResult>? children = null) => new()
    {
        Id = id, Type = children is null ? "request" : "include", Name = id, Passed = passed, Error = error, Assertions = assertions ?? [], Children = children,
    };

    [Fact]
    public void The_report_separates_pass_fail_and_error_and_summarizes_them()
    {
        var report = ReportFormatterService.Format(new CollectionExecutionResult
        {
            CollectionName = "Demo", RootPath = "/demo", StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
            TestCases =
            [
                Test("ok", Step("a", true, assertions: [Assertion(RunOutcome.Passed)])),
                Test("bad", Step("b", false, assertions: [Assertion(RunOutcome.Failed, "Expected '==' with value '1', actual '2'")])),
                Test("broken", Step("c", false, "Template variable 'x' was not found")),
            ],
        });

        Assert.Contains("Axiom Report - Demo", report);
        Assert.Contains("Total: 3 | Passed: 1 | Failed: 1 | Errors: 1", report);
        Assert.Contains("[PASS] ok", report);
        Assert.Contains("[FAIL] bad", report);
        Assert.Contains("[ERROR] broken", report);
        Assert.Contains("Error: Template variable 'x' was not found", report);
        Assert.Contains("Expected '==' with value '1', actual '2'", report);
    }

    [Fact]
    public void Assertions_show_their_path_and_aggregation_and_shared_steps_are_indented()
    {
        var report = ReportFormatterService.Format(new CollectionExecutionResult
        {
            CollectionName = "Demo", RootPath = "/demo", StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
            TestCases =
            [
                Test("t", Step("include", true, children: [Step("login", true, assertions: [Assertion(RunOutcome.Passed, path: "items.*.price", aggregate: "sum")])])),
            ],
        });

        Assert.Contains("source=body.items.*.price (sum)", report);
        var lines = report.Split(Environment.NewLine);
        var include = lines.First(l => l.Contains("include (include)"));
        var login = lines.First(l => l.Contains("login (request)"));
        Assert.True(login.Length - login.TrimStart().Length > include.Length - include.TrimStart().Length);
    }
}
