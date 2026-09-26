using System.Collections.Concurrent;
using System.Threading.Channels;
using Axiom.Models;
using Axiom.Parsing;
using Axiom.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Runtime;

/// <summary>
/// Runs every test of a collection, several at a time, and reports the results.
/// </summary>
public sealed class CollectionRunner(YamlCollectionLoader loader, IServiceScopeFactory scopeFactory)
{
    /// <summary>
    /// Runs the collection in <paramref name="folderPath"/> with default options.
    /// </summary>
    public Task<CollectionExecutionResult> RunAsync(string folderPath, CancellationToken cancellationToken = default) =>
        RunAsync(folderPath, new RunOptions(), cancellationToken);

    /// <summary>
    /// Runs the collection in <paramref name="folderPath"/>. Secrets are resolved first, so a missing secret fails the run before any step executes.
    /// </summary>
    public async Task<CollectionExecutionResult> RunAsync(string folderPath, RunOptions options, CancellationToken cancellationToken = default)
    {
        var loaded = loader.Load(folderPath, TestFilter(options.Tests));
        var startedAt = DateTimeOffset.UtcNow;

        // One scope per run: HTTP client and cached DB connections live exactly as long as the run.
        await using var scope = scopeFactory.CreateAsyncScope();
        var testExecutor = scope.ServiceProvider.GetRequiredService<TestCaseExecutor>();
        var secrets = await ResolveSecretsAsync(scope, loaded.Collection, options, cancellationToken);

        var shared = new SharedStepsLibrary(loaded.SharedSteps);
        var testCases = loaded.TestCases;
        options.OnStarted?.Invoke(testCases.Count);

        var maxParallel = Math.Max(1, loaded.Collection.RunSettings.MaxParallelTestCases);
        var channel = Channel.CreateBounded<TestCaseDefinition>(new BoundedChannelOptions(Math.Max(2, maxParallel * 2))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false,
        });

        var results = new ConcurrentBag<TestCaseExecutionResult>();

        var producer = Task.Run(async () =>
        {
            foreach (var testCase in testCases)
            {
                await channel.Writer.WriteAsync(testCase, cancellationToken);
            }

            channel.Writer.Complete();
        }, cancellationToken);

        var workers = Enumerable.Range(0, maxParallel)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var testCase in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    var result = await testExecutor.ExecuteAsync(loaded.Collection, testCase, secrets, shared, cancellationToken);
                    results.Add(result);
                    options.OnTestCompleted?.Invoke(result);
                }
            }, cancellationToken))
            .ToArray();

        await producer;
        await Task.WhenAll(workers);

        var orderedResults = results
            .OrderBy(r => r.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CollectionExecutionResult
        {
            CollectionName = loaded.Collection.Name,
            RootPath = loaded.RootPath,
            TestCases = orderedResults,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Runs the steps of <paramref name="test"/> (which need not be saved) up to and including the one at
    /// <paramref name="throughStep"/>, with the collection's variables, secrets and shared steps, and returns
    /// what the last step that ran received. Used to build a test against a real response.
    /// </summary>
    public async Task<StepPreview> PreviewAsync(string folderPath, TestCaseDefinition test, int throughStep, RunOptions options, CancellationToken cancellationToken = default)
    {
        if (throughStep < 0 || throughStep >= test.Steps.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(throughStep), $"The test has no step {throughStep + 1}.");
        }

        // The collection settings and shared steps only: other tests are not needed, and a broken one must not get in the way.
        var loaded = loader.Load(folderPath, testFilter: _ => false);

        await using var scope = scopeFactory.CreateAsyncScope();
        var testExecutor = scope.ServiceProvider.GetRequiredService<TestCaseExecutor>();
        var secrets = await ResolveSecretsAsync(scope, loaded.Collection, options, cancellationToken);

        var partial = new TestCaseDefinition
        {
            Name = test.Name,
            Description = test.Description,
            Variables = test.Variables,
            Steps = test.Steps.Take(throughStep + 1).ToList(),
            SourceFile = test.SourceFile,
        };
        var (result, variables) = await testExecutor.ExecuteWithVariablesAsync(
            loaded.Collection, partial, secrets, new SharedStepsLibrary(loaded.SharedSteps), cancellationToken);

        var last = result.Steps.Count > 0 ? result.Steps[^1] : null;
        return new StepPreview
        {
            Result = result,
            Response = last is null ? null : ReadResponse(last, variables, secrets),
        };
    }

    private static async Task<RunSecrets> ResolveSecretsAsync(AsyncServiceScope scope, CollectionDefinition collection, RunOptions options, CancellationToken cancellationToken)
    {
        scope.ServiceProvider.GetRequiredService<LocalSecretProvider>().Load(options.LocalSecrets);
        return await scope.ServiceProvider.GetRequiredService<SecretResolver>()
            .ResolveAsync(collection.Secrets, options.Environment, cancellationToken);
    }

    private static Func<string, bool>? TestFilter(IReadOnlyCollection<string>? tests)
    {
        if (tests is null || tests.Count == 0)
        {
            return null;
        }

        var fileNames = tests
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => CollectionPaths.ToTestFileName(name.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return fileNames.Contains;
    }

    /// <summary>
    /// What a step received, read from the variables it stores (<c>&lt;id&gt;_status</c>, <c>&lt;id&gt;_rows</c>, ...), with secrets masked.
    /// </summary>
    private static StepResponse? ReadResponse(StepExecutionResult step, IReadOnlyDictionary<string, object?> variables, RunSecrets secrets)
    {
        if (variables.TryGetValue($"{step.Id}_status", out var status) && status is int statusCode)
        {
            var text = variables.GetValueOrDefault($"{step.Id}_response_text") as string ?? string.Empty;
            var truncated = text.Length > StepPreviewLimits.MaxBodyChars;
            var headers = variables.GetValueOrDefault($"{step.Id}_headers") as IReadOnlyDictionary<string, object?>
                ?? new Dictionary<string, object?>();
            return new StepResponse
            {
                StepId = step.Id,
                Status = statusCode,
                Headers = headers.ToDictionary(pair => pair.Key, pair => secrets.Mask(pair.Value?.ToString()) ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                Body = secrets.Mask(truncated ? text[..StepPreviewLimits.MaxBodyChars] : text),
                BodyTruncated = truncated,
                DurationMs = step.DurationMs,
            };
        }

        if (variables.TryGetValue($"{step.Id}_rows", out var value) && value is List<Dictionary<string, object?>> rows)
        {
            return new StepResponse
            {
                StepId = step.Id,
                Rows = rows.Take(StepPreviewLimits.MaxRows)
                    .Select(IReadOnlyDictionary<string, object?> (row) => row.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value is string cell ? secrets.Mask(cell) : pair.Value,
                        StringComparer.OrdinalIgnoreCase))
                    .ToList(),
                RowCount = rows.Count,
                DurationMs = step.DurationMs,
            };
        }

        return null;
    }
}
