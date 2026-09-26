using Axiom.Models;
using Axiom.Secrets;

namespace Axiom.Runtime;

/// <summary>
/// Everything a step needs while it runs: the collection, secrets, variables and the services to run nested steps.
/// </summary>
public sealed class StepExecutionContext
{
    /// <summary>
    /// The collection being run.
    /// </summary>
    public required CollectionDefinition Collection { get; init; }

    /// <summary>
    /// The secrets resolved for this run.
    /// </summary>
    public required RunSecrets Secrets { get; init; }

    /// <summary>
    /// Variables visible to templates; steps add their results here for later steps to use.
    /// </summary>
    public required Dictionary<string, object?> Variables { get; init; }

    /// <summary>
    /// Runs nested steps (used by steps such as include).
    /// </summary>
    public required StepRunner Runner { get; init; }

    /// <summary>
    /// The shared step groups of the collection.
    /// </summary>
    public required SharedStepsLibrary Shared { get; init; }

    /// <summary>
    /// Ids of the shared step groups currently being run, outermost first; used to detect cycles.
    /// </summary>
    public IReadOnlyList<string> IncludeChain { get; init; } = [];

    /// <summary>
    /// The context for running a shared group: same collection, secrets and services, but its own variables and include chain.
    /// </summary>
    public StepExecutionContext ForInclude(Dictionary<string, object?> variables, IReadOnlyList<string> chain) => new()
    {
        Collection = Collection,
        Secrets = Secrets,
        Variables = variables,
        Runner = Runner,
        Shared = Shared,
        IncludeChain = chain,
    };
}
