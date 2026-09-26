namespace Axiom.Runtime;

public sealed class AssertionResult
{
    public required string Source { get; init; }
    public string? Path { get; init; }
    public string? Aggregate { get; init; }
    public required string Operator { get; init; }
    public required object? Expected { get; init; }
    public required object? Actual { get; init; }
    public required RunOutcome Outcome { get; init; }
    public bool Passed => Outcome == RunOutcome.Passed;

    /// <summary>Why the assertion failed, or why it could not be evaluated (see <see cref="Outcome"/>).</summary>
    public string? Error { get; init; }
}
