using Axiom.Models;
using Axiom.Secrets;

namespace Axiom.Runtime;

public sealed class StepExecutionContext
{
    public required CollectionDefinition Collection { get; init; }

    public required RunSecrets Secrets { get; init; }

    /// <summary>Variables visible to templates; steps add their results here for later steps to use.</summary>
    public required Dictionary<string, object?> Variables { get; init; }
}
