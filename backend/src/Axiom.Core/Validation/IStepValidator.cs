using Axiom.Documents;

namespace Axiom.Validation;

/// <summary>
/// Checks the fields a step type requires when a test is saved. Pair one with each <c>IStepExecutor</c>.
/// </summary>
public interface IStepValidator
{
    /// <summary>
    /// The step type this validator checks.
    /// </summary>
    string StepType { get; }

    /// <summary>
    /// Returns the problems in <paramref name="step"/>; <paramref name="fieldPrefix"/> is prepended to field names.
    /// </summary>
    IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix);
}
