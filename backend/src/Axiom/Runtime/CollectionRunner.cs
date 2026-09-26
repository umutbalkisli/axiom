using System.Collections.Concurrent;
using System.Threading.Channels;
using Axiom.Models;
using Axiom.Parsing;

namespace Axiom.Runtime;

public sealed class CollectionRunner(YamlCollectionLoader loader)
{
    public async Task<CollectionExecutionResult> RunAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var loaded = loader.Load(folderPath);
        var startedAt = DateTimeOffset.UtcNow;

        using var httpClient = new HttpClient();
        await using var dbQueryExecutor = new DbQueryExecutor();
        var testExecutor = new TestCaseExecutor(httpClient, dbQueryExecutor);

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
                    var result = await testExecutor.ExecuteAsync(loaded.Collection, testCase, cancellationToken);
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