namespace Axiom.Documents;

/// <summary>
/// A request to create or update a test.
/// </summary>
public sealed class SaveTestCaseRequest
{
    /// <summary>
    /// The file being edited. Leave empty to create a new test; a new, unique file name is chosen.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Preferred base for the file name of a new test (defaults to the test name).
    /// </summary>
    public string? FileNameHint { get; set; }
    /// <summary>
    /// Display name of the test.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Optional description of the test.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// The endpoint the test is about; used to group tests.
    /// </summary>
    public string? Endpoint { get; set; }
    /// <summary>
    /// The HTTP method of the endpoint; used to group tests.
    /// </summary>
    public string? Method { get; set; }
    /// <summary>
    /// Variables local to this test, by name.
    /// </summary>
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The steps of the test, in order.
    /// </summary>
    public List<StepDocument> Steps { get; set; } = [];
}
