namespace Axiom.Runtime;

public sealed class StepExecutionResult
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<AssertionResult> Assertions { get; init; }
    public bool Passed { get; init; }
    public string? Error { get; init; }
    public int? StatusCode { get; init; }
    public double DurationMs { get; init; }
    public int? RowCount { get; init; }

    /// <summary>For <c>include</c> steps: the results of the shared steps that ran.</summary>
    public IReadOnlyList<StepExecutionResult>? Children { get; init; }
}