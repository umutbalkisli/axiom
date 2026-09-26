using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Runs a list of steps in order with per-step timeouts, stopping at the first failure. Used for tests and for shared step groups.
/// </summary>
public sealed class StepRunner
{
    private readonly Dictionary<string, IStepExecutor> _stepExecutors;

    /// <summary>
    /// Creates a runner that dispatches steps to <paramref name="stepExecutors"/> by type.
    /// </summary>
    public StepRunner(IEnumerable<IStepExecutor> stepExecutors)
    {
        _stepExecutors = stepExecutors.ToDictionary(e => e.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs <paramref name="steps"/> in order, stopping at the first one that does not pass. Each step gets the collection's step timeout.
    /// </summary>
    public async Task<List<StepExecutionResult>> RunAsync(StepExecutionContext context, IReadOnlyList<StepDefinition> steps, CancellationToken cancellationToken)
    {
        var results = new List<StepExecutionResult>(steps.Count);

        foreach (var step in steps)
        {
            _stepExecutors.TryGetValue(step.Type, out var executor);

            // A step that runs other steps (include) leaves timeouts to those steps.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (executor?.ManagesTimeout != true)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(context.Collection.RunSettings.StepTimeoutSeconds));
            }

            StepExecutionResult result;
            try
            {
                result = executor is null
                    ? StepResults.Error(step, $"Unsupported step type '{step.Type}'")
                    : await executor.ExecuteAsync(context, step, cts.Token);
            }
            catch (Exception ex)
            {
                result = StepResults.Error(step, ex.Message);
            }

            results.Add(context.Secrets.Mask(result));
            if (!result.Passed)
            {
                break;
            }
        }

        return results;
    }
}
