using Axiom.Documents;

namespace Axiom.Validation;

/// <summary>
/// Checks the fields a step type requires when a test is saved. Pair one with each <c>IStepExecutor</c>.
/// </summary>
public interface IStepValidator
{
    string StepType { get; }

    IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix);
}
