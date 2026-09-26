namespace Axiom.Runtime;

public sealed class CollectionExecutionResult
{
    public required string CollectionName { get; init; }
    public required string RootPath { get; init; }
    public required IReadOnlyList<TestCaseExecutionResult> TestCases { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }

    public int TotalCount => TestCases.Count;
    public int PassedCount => TestCases.Count(t => t.Outcome == RunOutcome.Passed);

    /// <summary>Tests that ran and did not pass.</summary>
    public int FailedCount => TestCases.Count(t => t.Outcome == RunOutcome.Failed);

    /// <summary>Tests that could not be evaluated (see <see cref="RunOutcome.Error"/>).</summary>
    public int ErrorCount => TestCases.Count(t => t.Outcome == RunOutcome.Error);

    /// <summary>Everything that did not pass: failed plus errors. Decides the exit code.</summary>
    public int UnsuccessfulCount => TotalCount - PassedCount;

    public double SuccessRate => TotalCount == 0 ? 0 : (double)PassedCount / TotalCount * 100;
}