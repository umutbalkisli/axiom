namespace Axiom.Models;

public sealed class TestCaseDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StepDefinition> Steps { get; set; } = [];
    public string SourceFile { get; set; } = string.Empty;
}