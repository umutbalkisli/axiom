using Axiom.Models;

namespace Axiom.Documents;

/// <summary>
/// The editable content of <c>collection.yaml</c>, as exchanged with the desktop app.
/// </summary>
public sealed class CollectionDocument
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
    /// Variables available to every test, by name.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Database connections by name (provider and connection string).
    /// </summary>
    public Dictionary<string, object?> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// References to secrets by name; the values are never stored here.
    /// </summary>
    public Dictionary<string, SecretReference> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Parallelism and timeout settings for a run.
    /// </summary>
    public RunSettingsDocument RunSettings { get; set; } = new();
    /// <summary>
    /// Defaults applied to every request step.
    /// </summary>
    public RequestDefaultsDocument RequestDefaults { get; set; } = new();
}