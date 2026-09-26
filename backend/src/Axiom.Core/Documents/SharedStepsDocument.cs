namespace Axiom.Documents;

/// <summary>
/// A group of shared steps as stored in <c>shared/*.shared.yaml</c>.
/// </summary>
public sealed class SharedStepsDocument
{
    /// <summary>
    /// Display name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the group.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// <c>once</c> or <c>each</c>; see <see cref="Axiom.Models.SharedStepsDefinition.Run"/>.
    /// </summary>
    public string Run { get; set; } = "each";
    /// <summary>
    /// The steps of the group, in order.
    /// </summary>
    public List<StepDocument> Steps { get; set; } = [];
}

/// <summary>
/// A request to create or update a group of shared steps.
/// </summary>
public sealed class SaveSharedStepsRequest
{
    /// <summary>
    /// The file being edited. Leave empty to create a new group; a unique file name is chosen.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Display name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the group.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Either <c>once</c> (once per run, result shared) or <c>each</c> (inside every including test). Empty means <c>each</c>.
    /// </summary>
    public string? Run { get; set; }
    /// <summary>
    /// The steps of the group, in order.
    /// </summary>
    public List<StepDocument> Steps { get; set; } = [];
}

/// <summary>
/// A shared steps group as shown in lists.
/// </summary>
public sealed class SharedStepsListItem
{
    /// <summary>
    /// File name including the suffix.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>
    /// File name without the suffix; the value an <c>include</c> step refers to.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Display name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the group.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// <c>once</c> or <c>each</c>.
    /// </summary>
    public string Run { get; set; } = "each";
    /// <summary>
    /// Number of steps in the group.
    /// </summary>
    public int StepCount { get; set; }

    /// <summary>
    /// Names of the variables the group saves, so the builder can suggest them after an include.
    /// </summary>
    public List<string> Provides { get; set; } = [];
}
