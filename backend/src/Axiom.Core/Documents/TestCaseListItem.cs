namespace Axiom.Documents;

/// <summary>
/// A test as shown in lists.
/// </summary>
public sealed class TestCaseListItem
{
    /// <summary>
    /// File name including the suffix.
    /// </summary>
    public required string FileName { get; init; }
    /// <summary>
    /// File name without the suffix.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Display name of the test.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// The endpoint the test is about.
    /// </summary>
    public string? Endpoint { get; init; }
    /// <summary>
    /// The HTTP method of the endpoint.
    /// </summary>
    public string? Method { get; init; }
}