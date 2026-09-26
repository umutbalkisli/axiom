namespace Axiom.Documents;

public sealed class SaveTestCaseRequest
{
    /// <summary>The file being edited. Leave empty to create a new test; a new, unique file name is chosen.</summary>
    public string? FileName { get; set; }

    /// <summary>Preferred base for the file name of a new test (defaults to the test name).</summary>
    public string? FileNameHint { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Endpoint { get; set; }
    public string? Method { get; set; }
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StepDocument> Steps { get; set; } = [];
}
