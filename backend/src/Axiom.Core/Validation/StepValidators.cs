using Axiom.Documents;
using Axiom.Runtime;

namespace Axiom.Validation;

public sealed class RequestStepValidator : IStepValidator
{
    public string StepType => RequestStepExecutor.StepType;

    public IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix)
    {
        if (string.IsNullOrWhiteSpace(step.Method))
        {
            yield return new FieldError($"{fieldPrefix}.method", "HTTP method is required for request step.");
        }

        if (string.IsNullOrWhiteSpace(step.Url))
        {
            yield return new FieldError($"{fieldPrefix}.url", "URL is required for request step.");
        }
    }
}

public sealed class DbQueryStepValidator : IStepValidator
{
    public string StepType => DbQueryStepExecutor.StepType;

    public IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix)
    {
        if (string.IsNullOrWhiteSpace(step.Connection))
        {
            yield return new FieldError($"{fieldPrefix}.connection", "Connection is required for db_query step.");
        }

        if (string.IsNullOrWhiteSpace(step.Sql))
        {
            yield return new FieldError($"{fieldPrefix}.sql", "SQL is required for db_query step.");
        }
    }
}

public sealed class IncludeStepValidator : IStepValidator
{
    public string StepType => IncludeStepExecutor.StepType;

    public IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix)
    {
        if (string.IsNullOrWhiteSpace(step.Ref))
        {
            yield return new FieldError($"{fieldPrefix}.ref", "Choose the shared steps to include.");
        }
    }
}
