using Axiom.Models;

namespace Axiom.Runtime;

internal static class StepResults
{
    public static StepExecutionResult Error(StepDefinition step, string message) => new()
    {
        Id = step.Id,
        Type = step.Type,
        Name = step.Name ?? step.Id,
        Assertions = [],
        Passed = false,
        Error = message,
    };
}
