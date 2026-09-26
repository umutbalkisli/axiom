namespace Axiom.Models;

/// <summary>A named list of steps that tests can reuse with an <c>include</c> step (for example "get an auth token").</summary>
public sealed class SharedStepsDefinition
{
    public const string RunOnce = "once";
    public const string RunEach = "each";

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary><c>once</c>: run a single time per run and share the variables it produces. <c>each</c> (default): run inside every including test.</summary>
    public string Run { get; set; } = RunEach;

    public List<StepDefinition> Steps { get; set; } = [];

    public string SourceFile { get; set; } = string.Empty;

    public bool RunsOnce => string.Equals(Run?.Trim(), RunOnce, StringComparison.OrdinalIgnoreCase);
}
