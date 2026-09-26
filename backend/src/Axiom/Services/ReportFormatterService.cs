using Axiom.Runtime;

namespace Axiom.Services;

internal static class ReportFormatterService
{
    private const string HeaderLine = "============================================================";
    private const string DividerLine = "------------------------------------------------------------";

    public static string Format(CollectionExecutionResult result)
    {
        var totalDuration = (result.CompletedAt - result.StartedAt).TotalMilliseconds;
        var lines = new List<string>
        {
            HeaderLine,
            $"Axiom Report - {result.CollectionName}",
            HeaderLine,
            $"Collection Path : {result.RootPath}",
            $"Started At      : {result.StartedAt:O}",
            $"Completed At    : {result.CompletedAt:O}",
            $"Duration (ms)   : {totalDuration:F0}",
            string.Empty,
            $"Total: {result.TotalCount} | Passed: {result.PassedCount} | Failed: {result.FailedCount} | Errors: {result.ErrorCount} | Success: {result.SuccessRate:F2}%",
            DividerLine,
        };

        foreach (var test in result.TestCases)
        {
            AddTestLines(lines, test);
            lines.Add(DividerLine);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddTestLines(List<string> lines, TestCaseExecutionResult test)
    {
        var status = Label(test.Outcome);
        var duration = (test.CompletedAt - test.StartedAt).TotalMilliseconds;
        lines.Add($"[{status}] {test.Name} ({duration:F0} ms)");
        lines.Add($"  File: {test.SourceFile}");

        foreach (var step in test.Steps)
        {
            AddStepLines(lines, step);
        }
    }

    private static void AddStepLines(List<string> lines, StepExecutionResult step, int depth = 0)
    {
        var indent = new string(' ', depth * 4);
        var stepStatus = step.Outcome switch { RunOutcome.Passed => "OK", RunOutcome.Failed => "FAIL", _ => "ERR" };
        lines.Add($"{indent}    - {stepStatus} {step.Id} ({step.Type}) [{step.DurationMs:F0} ms]");

        if (!string.IsNullOrWhiteSpace(step.Error))
        {
            lines.Add($"{indent}      Error: {step.Error}");
        }

        foreach (var assertion in step.Assertions)
        {
            AddAssertionLines(lines, assertion, indent);
        }

        foreach (var child in step.Children ?? [])
        {
            AddStepLines(lines, child, depth + 1);
        }
    }

    private static void AddAssertionLines(List<string> lines, AssertionResult assertion, string indent)
    {
        var status = Label(assertion.Outcome);
        lines.Add($"{indent}      [{status}] Assertion: source={assertion.Source}{(string.IsNullOrEmpty(assertion.Path) ? string.Empty : "." + assertion.Path)}{(string.IsNullOrEmpty(assertion.Aggregate) ? string.Empty : $" ({assertion.Aggregate})")}, op={assertion.Operator}, expected={assertion.Expected}, actual={assertion.Actual}");

        if (!assertion.Passed && !string.IsNullOrWhiteSpace(assertion.Error))
        {
            lines.Add($"{indent}        Error: {assertion.Error}");
        }
    }

    /// <summary>
    /// ERROR marks something that could not be evaluated, as opposed to FAIL for something that ran and did not pass.
    /// </summary>
    private static string Label(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Passed => "PASS",
        RunOutcome.Failed => "FAIL",
        _ => "ERROR",
    };
}
