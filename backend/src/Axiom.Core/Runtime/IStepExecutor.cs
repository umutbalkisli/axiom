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

    /// <summary>
    /// True for steps that run other steps and so leave the per-step timeout to them.
    /// </summary>
    bool ManagesTimeout => false;

    /// <summary>
    /// Runs the step and returns its result. May add values to <see cref="StepExecutionContext.Variables"/>.
    /// </summary>
    Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken);
}
