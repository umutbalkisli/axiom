using Axiom.Documents;

namespace Axiom.Validation;

public sealed class TestCaseValidator
{
    private readonly Dictionary<string, IStepValidator> _stepValidators;

    public TestCaseValidator(IEnumerable<IStepValidator> stepValidators)
    {
        _stepValidators = stepValidators.ToDictionary(v => v.StepType, StringComparer.OrdinalIgnoreCase);
    }

    public List<FieldError> Validate(SaveTestCaseRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", "Test case name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            errors.Add(new FieldError("endpoint", "Endpoint is required."));
        }

        if (request.Steps.Count == 0)
        {
            errors.Add(new FieldError("steps", "At least one step is required."));
            return errors;
        }

        for (var index = 0; index < request.Steps.Count; index++)
        {
            ValidateStep(request.Steps[index], index, errors);
        }

        return errors;
    }

    private void ValidateStep(StepDocument step, int index, List<FieldError> errors)
    {
        var prefix = $"steps[{index}]";

        if (string.IsNullOrWhiteSpace(step.Id))
        {
            errors.Add(new FieldError($"{prefix}.id", "Step id is required."));
        }

        if (step.Type is null || !_stepValidators.TryGetValue(step.Type.Trim(), out var validator))
        {
            errors.Add(new FieldError($"{prefix}.type", $"Step type must be one of: {string.Join(", ", _stepValidators.Keys)}."));
            return;
        }

        errors.AddRange(validator.Validate(step, prefix));

        for (var assertionIndex = 0; assertionIndex < step.Assert.Count; assertionIndex++)
        {
            ValidateAssertion(step.Assert[assertionIndex], prefix, assertionIndex, errors);
        }
    }

    private static void ValidateAssertion(AssertionDocument assertion, string stepPrefix, int assertionIndex, List<FieldError> errors)
    {
        var assertionPrefix = $"{stepPrefix}.assert[{assertionIndex}]";

        if (string.IsNullOrWhiteSpace(assertion.Source))
        {
            errors.Add(new FieldError($"{assertionPrefix}.source", "Assertion source is required."));
        }

        if (string.IsNullOrWhiteSpace(assertion.Operator))
        {
            errors.Add(new FieldError($"{assertionPrefix}.operator", "Assertion operator is required."));
        }
    }
}
