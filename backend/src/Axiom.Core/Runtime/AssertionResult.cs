namespace Axiom.Runtime;

public sealed class AssertionResult
{
    public required string Source { get; init; }
    public string? Path { get; init; }
    public string? Aggregate { get; init; }
    public required string Operator { get; init; }
    public required object? Expected { get; init; }
    public required object? Actual { get; init; }
    public required bool Passed { get; init; }
    public string? Error { get; init; }
}