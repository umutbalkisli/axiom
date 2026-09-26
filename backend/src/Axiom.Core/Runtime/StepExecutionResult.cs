namespace Axiom.Runtime;

/// <summary>
/// The result of running one step.
/// </summary>
public sealed class StepExecutionResult
{
    /// <summary>
    /// Id of the step that produced this result.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// The step type (<c>request</c>, <c>db_query</c>, <c>include</c>).
    /// </summary>
    public required string Type { get; init; }
    /// <summary>
    /// Display name of the step.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The result of every assertion of the step.
    /// </summary>
    public required IReadOnlyList<AssertionResult> Assertions { get; init; }
    /// <summary>
    /// True when the step ran and all its assertions passed.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Error when the step (or an assertion, or a shared step inside it) could not be evaluated; Failed when it ran but did not pass.
    /// </summary>
    public RunOutcome Outcome =>
        Error is not null || Assertions.Any(a => a.Outcome == RunOutcome.Error) || (Children?.Any(c => c.Outcome == RunOutcome.Error) ?? false)
            ? RunOutcome.Error
            : Passed ? RunOutcome.Passed : RunOutcome.Failed;
    /// <summary>
    /// Why the step could not run, if it could not.
    /// </summary>
    public string? Error { get; init; }
    /// <summary>
    /// HTTP status code (request steps).
    /// </summary>
    public int? StatusCode { get; init; }
    /// <summary>
    /// How long the step took, in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }
    /// <summary>
    /// Number of rows returned (db_query steps).
    /// </summary>
    public int? RowCount { get; init; }

    /// <summary>
    /// For <c>include</c> steps: the results of the shared steps that ran.
    /// </summary>
    public IReadOnlyList<StepExecutionResult>? Children { get; init; }
}