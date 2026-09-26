using Axiom.Models;

namespace Axiom.Parsing;

/// <summary>
/// A collection read from disk: its settings, tests and shared steps.
/// </summary>
public sealed class LoadedCollection
{
    /// <summary>
    /// The parsed <c>collection.yaml</c>.
    /// </summary>
    public required CollectionDefinition Collection { get; init; }
    /// <summary>
    /// All tests found in the tests folder.
    /// </summary>
    public required IReadOnlyList<TestCaseDefinition> TestCases { get; init; }
    /// <summary>
    /// Shared step groups by id.
    /// </summary>
    public IReadOnlyDictionary<string, SharedStepsDefinition> SharedSteps { get; init; } = new Dictionary<string, SharedStepsDefinition>();
    /// <summary>
    /// The full path of the collection folder.
    /// </summary>
    public required string RootPath { get; init; }
}