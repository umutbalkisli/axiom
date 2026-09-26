namespace Axiom.Runtime;

/// <summary>
/// The result of running a whole collection.
/// </summary>
public sealed class CollectionExecutionResult
{
    /// <summary>
    /// Name of the collection that ran.
    /// </summary>
    public required string CollectionName { get; init; }
    /// <summary>
    /// Full path of the collection folder.
    /// </summary>
    public required string RootPath { get; init; }
    /// <summary>
    /// The result of every test, ordered by file.
    /// </summary>
    public required IReadOnlyList<TestCaseExecutionResult> TestCases { get; init; }
    /// <summary>
    /// When the run started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }
    /// <summary>
    /// When the run finished.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Number of tests that ran.
    /// </summary>
    public int TotalCount => TestCases.Count;
    /// <summary>
    /// Tests that passed.
    /// </summary>
    public int PassedCount => TestCases.Count(t => t.Outcome == RunOutcome.Passed);

    /// <summary>
    /// Tests that ran and did not pass.
    /// </summary>
    public int FailedCount => TestCases.Count(t => t.Outcome == RunOutcome.Failed);

    /// <summary>
    /// Tests that could not be evaluated (see <see cref="RunOutcome.Error"/>).
    /// </summary>
    public int ErrorCount => TestCases.Count(t => t.Outcome == RunOutcome.Error);

    /// <summary>
    /// Everything that did not pass: failed plus errors. Decides the exit code.
    /// </summary>
    public int UnsuccessfulCount => TotalCount - PassedCount;

    /// <summary>
    /// Percentage of tests that passed.
    /// </summary>
    public double SuccessRate => TotalCount == 0 ? 0 : (double)PassedCount / TotalCount * 100;
}