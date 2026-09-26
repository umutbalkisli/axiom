using Axiom.Models;

namespace Axiom.Parsing;

public sealed class LoadedCollection
{
    public required CollectionDefinition Collection { get; init; }
    public required IReadOnlyList<TestCaseDefinition> TestCases { get; init; }
    public IReadOnlyDictionary<string, SharedStepsDefinition> SharedSteps { get; init; } = new Dictionary<string, SharedStepsDefinition>();
    public required string RootPath { get; init; }
}