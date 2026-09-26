namespace Axiom.Documents;

public sealed class TestCaseListItem
{
    public required string FileName { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Endpoint { get; init; }
    public string? Method { get; init; }
}