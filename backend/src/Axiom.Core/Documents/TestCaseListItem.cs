namespace Axiom.Documents;

/// <summary>
/// A test as shown in lists.
/// </summary>
public sealed class TestCaseListItem
{
    /// <summary>
    /// Path relative to the tests folder, including the suffix (<c>orders/create-order.test.yaml</c>).
    /// This is how the test is addressed everywhere.
    /// </summary>
    public required string FileName { get; init; }
    /// <summary>
    /// <see cref="FileName"/> without the suffix (<c>orders/create-order</c>).
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// The folder the test is in, relative to the tests folder; empty at the top level.
    /// </summary>
    public string Folder { get; init; } = string.Empty;
    /// <summary>
    /// The test's stable <c>id:</c>; null for a test written before ids existed.
    /// </summary>
    public string? TestId { get; init; }
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