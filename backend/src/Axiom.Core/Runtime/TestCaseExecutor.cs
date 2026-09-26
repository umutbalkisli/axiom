using Axiom.Models;
using Axiom.Secrets;

namespace Axiom.Runtime;

public sealed class TestCaseExecutor
{
    /// <summary>Reserved variable name under which secrets are exposed to templates.</summary>
    public const string SecretVariableName = "secret";

    private readonly Dictionary<string, IStepExecutor> _stepExecutors;

    public TestCaseExecutor(IEnumerable<IStepExecutor> stepExecutors)
    {
        _stepExecutors = stepExecutors.ToDictionary(e => e.Type, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TestCaseExecutionResult> ExecuteAsync(CollectionDefinition collection, TestCaseDefinition testCase, RunSecrets secrets, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        Dictionary<string, object?> variables;
        try
        {
            variables = BuildInitialVariables(collection, testCase, secrets);
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
        };
        var stepResults = new List<StepExecutionResult>(capacity: testCase.Steps.Count);

        foreach (var step in testCase.Steps)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(collection.RunSettings.StepTimeoutSeconds));

            StepExecutionResult result;
            try
            {
                result = _stepExecutors.TryGetValue(step.Type, out var executor)
                    ? await executor.ExecuteAsync(context, step, cts.Token)
                    : StepResults.Error(step, $"Unsupported step type '{step.Type}'");
            }
            catch (Exception ex)
            {
                result = StepResults.Error(step, ex.Message);
            }

            stepResults.Add(secrets.Mask(result));
            if (!result.Passed)
            {
                break;
            }
        }

        return new TestCaseExecutionResult
        {
            Name = testCase.Name,
            SourceFile = testCase.SourceFile,
            Steps = stepResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Dictionary<string, object?> BuildInitialVariables(CollectionDefinition collection, TestCaseDefinition testCase, RunSecrets secrets)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in collection.Variables.Concat(testCase.Variables))
        {
            // Variables may embed {{secret.name}}; expand them here so every later use sees the real value.
            variables[pair.Key] = pair.Value is string text ? secrets.Expand(text) : pair.Value;
        }

        variables[SecretVariableName] = secrets.AsTemplateVariables();
        return variables;
    }
}
