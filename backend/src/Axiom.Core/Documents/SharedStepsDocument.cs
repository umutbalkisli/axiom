namespace Axiom.Documents;

public sealed class SharedStepsDocument
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Run { get; set; } = "each";
    public List<StepDocument> Steps { get; set; } = [];
}

public sealed class SaveSharedStepsRequest
{
    /// <summary>The file being edited. Leave empty to create a new group; a unique file name is chosen.</summary>
    public string? FileName { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Run { get; set; }
    public List<StepDocument> Steps { get; set; } = [];
}

public sealed class SharedStepsListItem
{
    public string FileName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Run { get; set; } = "each";
    public int StepCount { get; set; }

    /// <summary>Names of the variables the group saves, so the builder can suggest them after an include.</summary>
    public List<string> Provides { get; set; } = [];
}
