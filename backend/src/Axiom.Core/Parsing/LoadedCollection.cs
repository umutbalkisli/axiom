using Axiom.Models;

namespace Axiom.Parsing;

public sealed class LoadedCollection
{
    public required CollectionDefinition Collection { get; init; }
    public required IReadOnlyList<TestCaseDefinition> TestCases { get; init; }
    public required string RootPath { get; init; }
}