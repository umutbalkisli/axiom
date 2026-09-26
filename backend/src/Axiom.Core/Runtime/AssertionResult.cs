namespace Axiom.Runtime;

/// <summary>
/// The result of evaluating one assertion.
/// </summary>
public sealed class AssertionResult
{
    /// <summary>
    /// The source the value came from.
    /// </summary>
    public required string Source { get; init; }
    /// <summary>
    /// The path applied to the source, if any.
    /// </summary>
    public string? Path { get; init; }
    /// <summary>
    /// The aggregation applied before the comparison, if any.
    /// </summary>
    public string? Aggregate { get; init; }
    /// <summary>
    /// The operator that was applied.
    /// </summary>
    public required string Operator { get; init; }
    /// <summary>
    /// The expected value after templates were resolved. Kept complete, however large.
    /// </summary>
    public required object? Expected { get; init; }
    /// <summary>
    /// The value that was found. Kept complete, however large.
    /// </summary>
    public required object? Actual { get; init; }
    /// <summary>
    /// Passed, Failed, or Error when the assertion could not be evaluated.
    /// </summary>
    public required RunOutcome Outcome { get; init; }
    /// <summary>
    /// True when <see cref="Outcome"/> is <see cref="RunOutcome.Passed"/>.
    /// </summary>
    public bool Passed => Outcome == RunOutcome.Passed;

    /// <summary>
    /// Why the assertion failed, or why it could not be evaluated (see <see cref="Outcome"/>).
    /// </summary>
    public string? Error { get; init; }
}
