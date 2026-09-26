namespace Axiom.Runtime;

/// <summary>
/// Options for a single run of a collection.
/// </summary>
public sealed class RunOptions
{
    /// <summary>
    /// Selects environment-specific secret sources (for example "local", "ci", "prod"). Null uses the defaults.
    /// </summary>
    public string? Environment { get; init; }

    /// <summary>
    /// Values for secrets whose provider is <c>local</c>, supplied by the caller for this run only.
    /// </summary>
    public IReadOnlyDictionary<string, string>? LocalSecrets { get; init; }
}
