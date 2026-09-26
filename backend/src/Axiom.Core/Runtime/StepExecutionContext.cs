using Axiom.Models;

namespace Axiom.Runtime;

public sealed class StepExecutionContext
{
    public required CollectionDefinition Collection { get; init; }

    /// <summary>Variables visible to templates; steps add their results here for later steps to use.</summary>
    public required Dictionary<string, object?> Variables { get; init; }
}
