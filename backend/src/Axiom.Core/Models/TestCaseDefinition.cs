using YamlDotNet.Serialization;

namespace Axiom.Models;

/// <summary>
/// A parsed test: its variables and steps.
/// </summary>
public sealed class TestCaseDefinition
{
    /// <summary>
    /// The test's stable identity (<c>id:</c>): given once when the test is created and never changed, so it survives
    /// renames and moves. Null for a test written before ids existed.
    /// </summary>
    public string? Id { get; set; }
    /// <summary>
    /// Display name of the test.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the test.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// The endpoint the test is about (e.g. <c>/todos/{id}</c>); shown in the desktop app, not used when running.
    /// </summary>
    public string? Endpoint { get; set; }
    /// <summary>
    /// The HTTP method of <see cref="Endpoint"/>; shown in the desktop app, not used when running.
    /// </summary>
    public string? Method { get; set; }
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
    [YamlIgnore]
    public string SourceFile { get; set; } = string.Empty;
    /// <summary>
    /// Where the test lives, relative to the tests folder (<c>orders/create-order.test.yaml</c>).
    /// </summary>
    [YamlIgnore]
    public string FileName { get; set; } = string.Empty;
}