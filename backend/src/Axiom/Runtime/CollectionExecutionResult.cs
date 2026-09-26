namespace Axiom.Runtime;

public sealed class CollectionExecutionResult
{
    public required string CollectionName { get; init; }
    public required string RootPath { get; init; }
    public required IReadOnlyList<TestCaseExecutionResult> TestCases { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }

    public int TotalCount => TestCases.Count;
    public int PassedCount => TestCases.Count(t => t.Passed);
    public int FailedCount => TotalCount - PassedCount;
    public double SuccessRate => TotalCount == 0 ? 0 : (double)PassedCount / TotalCount * 100;
}