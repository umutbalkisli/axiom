using Axiom.Documents;
using Axiom.Models;
using Axiom.Runtime;

namespace Axiom.Validation;

/// <summary>
/// Validates tests and shared steps before they are saved: names, step types, required fields, operators and aggregations.
/// </summary>
public sealed class TestCaseValidator
{
    private readonly Dictionary<string, IStepValidator> _stepValidators;
    private readonly HashSet<string> _aggregations;
    private readonly HashSet<string> _operators;

    /// <summary>
    /// Creates a validator that knows the given step types, aggregations and operators.
    /// </summary>
    public TestCaseValidator(IEnumerable<IStepValidator> stepValidators, IEnumerable<IAssertionAggregation> aggregations, IEnumerable<IAssertionOperator> operators)
    {
        _operators = operators.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        _stepValidators = stepValidators.ToDictionary(v => v.StepType, StringComparer.OrdinalIgnoreCase);
        _aggregations = aggregations.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns every problem found in a test.
    /// </summary>
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

        errors.AddRange(CollectionSettingsValidator.ValidateVariables(request.Variables));

        ValidateSteps(request.Steps, errors);
        return errors;
    }

    /// <summary>
    /// Returns every problem found in a shared steps group.
    /// </summary>
    public List<FieldError> Validate(SaveSharedStepsRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new FieldError("name", "Name is required."));
        }

        if (!string.IsNullOrWhiteSpace(request.Run)
            && !new[] { SharedStepsDefinition.RunOnce, SharedStepsDefinition.RunEach }.Contains(request.Run.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new FieldError("run", "Run must be 'once' or 'each'."));
        }

        ValidateSteps(request.Steps, errors);
        return errors;
    }

    /// <summary>
    /// Include steps must point at existing shared groups.
    /// </summary>
    public static IEnumerable<FieldError> ValidateIncludes(IReadOnlyList<StepDocument> steps, IReadOnlySet<string> sharedIds)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var reference = step.Ref?.Trim();
            if (string.Equals(step.Type, IncludeStepExecutor.StepType, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(reference)
                && !sharedIds.Contains(reference))
            {
                yield return new FieldError($"steps[{index}].ref", $"Shared steps '{reference}' do not exist.");
            }
        }
    }

    private void ValidateSteps(List<StepDocument> steps, List<FieldError> errors)
    {
        if (steps.Count == 0)
        {
            errors.Add(new FieldError("steps", "At least one step is required."));
            return;
        }

        for (var index = 0; index < steps.Count; index++)
        {
            ValidateStep(steps[index], index, errors);
        }
    }

    private void ValidateStep(StepDocument step, int index, List<FieldError> errors)
    {
        var prefix = $"steps[{index}]";

        if (string.IsNullOrWhiteSpace(step.Id))
        {
            errors.Add(new FieldError($"{prefix}.id", "Step id is required."));
        }

        if (!string.IsNullOrEmpty(step.SaveAs) && VariableName.Check(step.SaveAs) is { } saveAsProblem)
        {
            errors.Add(new FieldError($"{prefix}.saveAs", saveAsProblem));
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

    private void ValidateAssertion(AssertionDocument assertion, string stepPrefix, int assertionIndex, List<FieldError> errors)
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
        else if (!_operators.Contains(assertion.Operator.Trim()))
        {
            errors.Add(new FieldError($"{assertionPrefix}.operator", $"Unknown operator '{assertion.Operator}'. Use one of: {string.Join(", ", _operators)}."));
        }

        if (!string.IsNullOrWhiteSpace(assertion.Aggregate) && !_aggregations.Contains(assertion.Aggregate.Trim()))
        {
            errors.Add(new FieldError($"{assertionPrefix}.aggregate", $"Aggregation must be one of: {string.Join(", ", _aggregations)}."));
        }
    }
}
