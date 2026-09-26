using System.Collections.Concurrent;
using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>What a shared-steps group produced when it ran: its step results and the variables it created or changed.</summary>
public sealed record SharedOutcome(IReadOnlyList<StepExecutionResult> Results, IReadOnlyDictionary<string, object?> Exported);

/// <summary>The shared step groups of a collection for one run, including the results of groups that run only once.</summary>
public sealed class SharedStepsLibrary(IReadOnlyDictionary<string, SharedStepsDefinition> definitions)
{
    private readonly Dictionary<string, SharedStepsDefinition> _definitions = new(definitions, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<SharedOutcome>>> _onceResults = new(StringComparer.OrdinalIgnoreCase);

    public static SharedStepsLibrary Empty { get; } = new(new Dictionary<string, SharedStepsDefinition>());

    public bool TryGet(string id, out SharedStepsDefinition definition) => _definitions.TryGetValue(id, out definition!);

    /// <summary>Runs <paramref name="run"/> for the first caller only; every other caller (in parallel tests too) awaits that same result.</summary>
    public Task<SharedOutcome> RunOnceAsync(string id, Func<Task<SharedOutcome>> run) =>
        _onceResults.GetOrAdd(id, _ => new Lazy<Task<SharedOutcome>>(run)).Value;
}
