namespace Axiom.Runtime;

public sealed class TestCaseExecutionResult
{
    public required string Name { get; init; }
    public required string SourceFile { get; init; }
    public required IReadOnlyList<StepExecutionResult> Steps { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool Passed => Steps.All(s => s.Passed);
}