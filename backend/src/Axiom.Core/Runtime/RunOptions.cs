namespace Axiom.Runtime;

/// <summary>
/// Options for one run of a collection.
/// </summary>
public sealed class RunOptions
{
    /// <summary>
    /// Environment that selects secret sources (see a secret's <c>environments</c>); null uses the defaults.
    /// </summary>
    public string? Environment { get; init; }

    /// <summary>
    /// Values for secrets whose provider is <c>local</c>, by key.
    /// </summary>
    public IReadOnlyDictionary<string, string>? LocalSecrets { get; init; }

    /// <summary>
    /// Runs only these tests, by file name (<c>get-user.test.yaml</c>) or id (<c>get-user</c>); null or empty runs every test.
    /// </summary>
    public IReadOnlyCollection<string>? Tests { get; init; }

    /// <summary>
    /// Called once the tests to run are known, with their number, before any of them starts.
    /// </summary>
    public Action<int>? OnStarted { get; init; }

    /// <summary>
    /// Called as soon as each test finishes. Tests run in parallel, so this is called from several threads at once.
    /// </summary>
    public Action<TestCaseExecutionResult>? OnTestCompleted { get; init; }
}
