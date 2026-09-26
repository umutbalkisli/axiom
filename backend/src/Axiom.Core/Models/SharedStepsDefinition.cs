using YamlDotNet.Serialization;

namespace Axiom.Models;

/// <summary>
/// A named list of steps that tests can reuse with an <c>include</c> step (for example "get an auth token").
/// </summary>
public sealed class SharedStepsDefinition
{
    /// <summary>
    /// The <see cref="Run"/> value for a group that runs once per run.
    /// </summary>
    public const string RunOnce = "once";
    /// <summary>
    /// The <see cref="Run"/> value for a group that runs inside every including test.
    /// </summary>
    public const string RunEach = "each";

    /// <summary>
    /// Display name of the group.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the group.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// <c>once</c>: run a single time per run and share the variables it produces. <c>each</c> (default): run inside every including test.
    /// </summary>
    public string Run { get; set; } = RunEach;

    /// <summary>
    /// The steps of the group, in order.
    /// </summary>
    public List<StepDefinition> Steps { get; set; } = [];

    /// <summary>
    /// The file the group was loaded from.
    /// </summary>
    [YamlIgnore]
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// True when the group runs once per run and its result is shared.
    /// </summary>
    public bool RunsOnce => string.Equals(Run?.Trim(), RunOnce, StringComparison.OrdinalIgnoreCase);
}
