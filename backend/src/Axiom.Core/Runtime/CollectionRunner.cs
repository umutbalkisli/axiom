using System.Collections.Concurrent;
using System.Threading.Channels;
using Axiom.Models;
using Axiom.Parsing;
using Axiom.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Runtime;

/// <summary>
/// Creates a runner that loads collections with <paramref name="loader"/>.
/// </summary>
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
        var loaded = loader.Load(folderPath);
        var startedAt = DateTimeOffset.UtcNow;

        // One scope per run: HTTP client and cached DB connections live exactly as long as the run.
        await using var scope = scopeFactory.CreateAsyncScope();
        var testExecutor = scope.ServiceProvider.GetRequiredService<TestCaseExecutor>();

        scope.ServiceProvider.GetRequiredService<LocalSecretProvider>().Load(options.LocalSecrets);
        var secrets = await scope.ServiceProvider.GetRequiredService<SecretResolver>()
            .ResolveAsync(loaded.Collection.Secrets, options.Environment, cancellationToken);

        var shared = new SharedStepsLibrary(loaded.SharedSteps);
        var testCases = loaded.TestCases;
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
}