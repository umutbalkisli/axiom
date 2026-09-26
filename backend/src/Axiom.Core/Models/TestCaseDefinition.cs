namespace Axiom.Models;

/// <summary>
/// A parsed test: its variables and steps.
/// </summary>
public sealed class TestCaseDefinition
{
    /// <summary>
    /// Display name of the test.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the test.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// Variables local to this test, by name.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The steps of the test, in order.
    /// </summary>
    public List<StepDefinition> Steps { get; set; } = [];
    /// <summary>
    /// The file the test was loaded from.
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;
}