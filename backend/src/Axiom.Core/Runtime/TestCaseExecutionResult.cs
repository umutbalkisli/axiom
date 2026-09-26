namespace Axiom.Runtime;

/// <summary>
/// The result of running one test.
/// </summary>
public sealed class TestCaseExecutionResult
{
    /// <summary>
    /// Display name of the test.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The file the test was loaded from.
    /// </summary>
    public required string SourceFile { get; init; }
    /// <summary>
    /// The results of the steps that ran; a run stops at the first step that does not pass.
    /// </summary>
    public required IReadOnlyList<StepExecutionResult> Steps { get; init; }
    /// <summary>
    /// When the test started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>
    /// When the test finished.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }
    /// <summary>
    /// True when every step passed.
    /// </summary>
    public bool Passed => Steps.All(s => s.Passed);

    /// <summary>
    /// Error when any step could not be evaluated, otherwise Passed or Failed.
    /// </summary>
    public RunOutcome Outcome =>
        Steps.Any(s => s.Outcome == RunOutcome.Error) ? RunOutcome.Error
        : Passed ? RunOutcome.Passed : RunOutcome.Failed;
}