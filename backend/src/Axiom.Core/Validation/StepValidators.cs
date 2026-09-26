using Axiom.Documents;
using Axiom.Runtime;

namespace Axiom.Validation;

/// <summary>
/// Checks that a request step has a method and a URL.
/// </summary>
public sealed class RequestStepValidator : IStepValidator
{
    /// <summary>
    /// The step type this validator checks.
    /// </summary>
    public string StepType => RequestStepExecutor.StepType;

    /// <summary>
    /// Returns the problems in the request step.
    /// </summary>
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

/// <summary>
/// Checks that a db_query step has a connection and SQL.
/// </summary>
public sealed class DbQueryStepValidator : IStepValidator
{
    /// <summary>
    /// The step type this validator checks.
    /// </summary>
    public string StepType => DbQueryStepExecutor.StepType;

    /// <summary>
    /// Returns the problems in the db_query step.
    /// </summary>
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

/// <summary>
/// Checks that an include step names the shared steps to run.
/// </summary>
public sealed class IncludeStepValidator : IStepValidator
{
    /// <summary>
    /// The step type this validator checks.
    /// </summary>
    public string StepType => IncludeStepExecutor.StepType;

    /// <summary>
    /// Returns the problems in the include step.
    /// </summary>
    public IEnumerable<FieldError> Validate(StepDocument step, string fieldPrefix)
    {
        if (string.IsNullOrWhiteSpace(step.Ref))
        {
            yield return new FieldError($"{fieldPrefix}.ref", "Choose the shared steps to include.");
        }
    }
}
