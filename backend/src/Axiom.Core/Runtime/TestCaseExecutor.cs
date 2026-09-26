using Axiom.Models;
using Axiom.Secrets;

namespace Axiom.Runtime;

/// <summary>
/// Creates an executor that runs steps with <paramref name="runner"/>.
/// </summary>
/// <summary>
/// Runs one test: builds its variables, then runs its steps.
/// </summary>
public sealed class TestCaseExecutor(StepRunner runner)
{
    /// <summary>
    /// Runs <paramref name="testCase"/> and returns its result. A test whose variables cannot be resolved reports that as an error step.
    /// </summary>
    public async Task<TestCaseExecutionResult> ExecuteAsync(
        CollectionDefinition collection,
        TestCaseDefinition testCase,
        RunSecrets secrets,
        SharedStepsLibrary shared,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        Dictionary<string, object?> variables;
        try
        {
            variables = StepVariables.Initial(collection, testCase.Variables, secrets);
        }
        catch (Exception ex)
        {
            return new TestCaseExecutionResult
            {
                Name = testCase.Name,
                SourceFile = testCase.SourceFile,
                Steps =
                [
                    new StepExecutionResult
                    {
                        Id = "variables",
                        Type = "setup",
                        Name = "Resolve variables",
                        Assertions = [],
                        Passed = false,
                        Error = secrets.Mask(ex.Message),
                    },
                ],
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
            };
        }

        var context = new StepExecutionContext
        {
            Collection = collection,
            Secrets = secrets,
            Variables = variables,
            Runner = runner,
            Shared = shared,
        };
        var stepResults = await runner.RunAsync(context, testCase.Steps, cancellationToken);

        return new TestCaseExecutionResult
        {
            Name = testCase.Name,
            SourceFile = testCase.SourceFile,
            Steps = stepResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }
}
