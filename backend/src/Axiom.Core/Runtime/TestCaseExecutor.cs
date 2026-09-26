using Axiom.Models;

namespace Axiom.Runtime;

public sealed class TestCaseExecutor
{
    private readonly Dictionary<string, IStepExecutor> _stepExecutors;

    public TestCaseExecutor(IEnumerable<IStepExecutor> stepExecutors)
    {
        _stepExecutors = stepExecutors.ToDictionary(e => e.Type, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TestCaseExecutionResult> ExecuteAsync(CollectionDefinition collection, TestCaseDefinition testCase, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var context = new StepExecutionContext
        {
            Collection = collection,
            Variables = BuildInitialVariables(collection, testCase),
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

            stepResults.Add(result);
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

    private static Dictionary<string, object?> BuildInitialVariables(CollectionDefinition collection, TestCaseDefinition testCase)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in collection.Variables)
        {
            variables[pair.Key] = pair.Value;
        }

        foreach (var pair in testCase.Variables)
        {
            variables[pair.Key] = pair.Value;
        }

        return variables;
    }
}
