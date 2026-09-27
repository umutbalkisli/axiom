namespace Axiom.Documents;

/// <summary>
/// A test as stored in <c>tests/*.test.yaml</c>.
/// </summary>
public sealed class TestCaseDocument
{
    /// <summary>
    /// The test's stable identity; see <see cref="Axiom.Models.TestCaseDefinition.Id"/>.
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