namespace Axiom.Documents;

public sealed class SaveTestCaseRequest
{
    public string? FileName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Endpoint { get; set; }
    public string? Method { get; set; }
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StepDocument> Steps { get; set; } = [];
}
