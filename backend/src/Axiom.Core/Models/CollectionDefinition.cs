namespace Axiom.Models;

/// <summary>
/// The parsed <c>collection.yaml</c>: shared settings for every test of a collection.
/// </summary>
public sealed class CollectionDefinition
{
    /// <summary>
    /// Display name of the collection.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the collection.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Database connections by name.
    /// </summary>
    public Dictionary<string, DbConnectionDefinition> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Variables available to every test, by name.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// References to secrets by name; the values are read from their providers at run time.
    /// </summary>
    public Dictionary<string, SecretReference> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Parallelism and timeout settings for a run.
    /// </summary>
    public RunSettings RunSettings { get; set; } = new();
    /// <summary>
    /// Defaults applied to every request step.
    /// </summary>
    public RequestDefaults RequestDefaults { get; set; } = new();
}
