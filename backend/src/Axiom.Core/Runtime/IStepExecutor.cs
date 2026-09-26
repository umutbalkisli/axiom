using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Runs one kind of test step. Register an implementation to add a new step <c>type</c>.
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// The <c>type</c> value in YAML this executor handles (case-insensitive).
    /// </summary>
    string Type { get; }

    Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken);
}
