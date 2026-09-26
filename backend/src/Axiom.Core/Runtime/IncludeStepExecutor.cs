using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Runs a shared step group inside the current test. Variables the group saves become available to the steps after it.
/// </summary>
public sealed class IncludeStepExecutor : IStepExecutor
{
    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public const string StepType = "include";

    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public string Type => StepType;

    /// <summary>
    /// True: the steps inside a shared group apply their own timeouts.
    /// </summary>
    public bool ManagesTimeout => true;

    /// <summary>
    /// Runs the referenced shared steps, once per run or inside this test depending on the group's run mode.
    /// </summary>
    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken)
    {
        var id = step.Ref?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return StepResults.Error(step, "Include step requires ref");
        }

        if (!context.Shared.TryGet(id, out var definition))
        {
            return StepResults.Error(step, $"Shared steps '{id}' were not found");
        }

        if (context.IncludeChain.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            return StepResults.Error(step, $"Shared steps '{id}' include themselves ({string.Join(" -> ", context.IncludeChain.Append(id))})");
        }

        var chain = context.IncludeChain.Append(id).ToList();
        IReadOnlyList<StepExecutionResult> children;

        if (definition.RunsOnce)
        {
            var outcome = await context.Shared.RunOnceAsync(id, () => RunIsolatedAsync(context, definition, chain, cancellationToken));
            foreach (var (name, value) in outcome.Exported)
            {
                // Each test gets its own copy of a response body so parallel tests never share one JSON tree.
                context.Variables[name] = value is LazyJson lazy ? lazy.Clone() : value;
            }

            children = outcome.Results;
        }
        else
        {
            children = await context.Runner.RunAsync(context.ForInclude(context.Variables, chain), definition.Steps, cancellationToken);
        }

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = string.IsNullOrWhiteSpace(step.Name) || step.Name == step.Id ? definition.Name : step.Name,
            Assertions = [],
            Passed = children.All(child => child.Passed),
            DurationMs = children.Sum(child => child.DurationMs),
            Children = children,
        };
    }

    /// <summary>
    /// A run-once group must not depend on the including test, so it starts from the collection variables only.
    /// </summary>
    private static async Task<SharedOutcome> RunIsolatedAsync(StepExecutionContext context, SharedStepsDefinition definition, List<string> chain, CancellationToken cancellationToken)
    {
        var variables = StepVariables.Initial(context.Collection, [], context.Secrets);
        var seed = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase);

        var results = await context.Runner.RunAsync(context.ForInclude(variables, chain), definition.Steps, cancellationToken);

        var exported = variables
            .Where(pair => pair.Key != StepVariables.SecretName)
            .Where(pair => !seed.TryGetValue(pair.Key, out var before) || !Equals(before, pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return new SharedOutcome(results, exported);
    }
}
