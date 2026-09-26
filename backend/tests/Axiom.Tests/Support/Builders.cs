using Axiom.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Tests.Support;

/// <summary>
/// Small factories that keep the tests short.
/// </summary>
public static class Build
{
    public static AssertionEngine Engine() => new(BuiltInAssertionOperators.All, BuiltInAssertionAggregations.All);

    public static AssertionDefinition Assertion(string source, string op, object? expected = null, string? path = null, string? aggregate = null, bool strict = false, bool caseSensitive = false, decimal? tolerance = null) =>
        new() { Source = source, Path = path, Operator = op, Expected = expected, Aggregate = aggregate, Strict = strict, CaseSensitive = caseSensitive, Tolerance = tolerance };

    public static StepDefinition Request(string id, string url, string method = "GET", params AssertionDefinition[] assertions) =>
        new() { Id = id, Name = id, Type = "request", Method = method, Url = url, Assert = assertions.ToList() };

    public static StepDefinition Include(string id, string reference) => new() { Id = id, Name = id, Type = "include", Ref = reference };

    public static CollectionDefinition Collection(int timeoutSeconds = 30) => new()
    {
        Name = "test",
        RunSettings = new RunSettings { MaxParallelTestCases = 4, StepTimeoutSeconds = timeoutSeconds },
    };

    /// <summary>
    /// The engine wired with dependency injection, but with every HTTP call answered by <paramref name="handler"/>.
    /// </summary>
    public static ServiceProvider Services(HttpMessageHandler handler)
    {
        var services = new ServiceCollection().AddAxiomCore();
        services.AddScoped<HttpClient>(_ => new HttpClient(handler, disposeHandler: false));
        return services.BuildServiceProvider();
    }

    public static StepExecutionContext Context(CollectionDefinition collection, StepRunner runner, SharedStepsLibrary? shared = null, RunSecrets? secrets = null, Dictionary<string, object?>? variables = null) => new()
    {
        Collection = collection,
        Secrets = secrets ?? RunSecrets.Empty,
        Variables = variables ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
        Runner = runner,
        Shared = shared ?? SharedStepsLibrary.Empty,
    };
}

/// <summary>
/// A step type for tests: counts how often it ran and can save a variable, fail, or wait.
/// </summary>
public sealed class ProbeExecutor(string type = "probe") : IStepExecutor
{
    private int _runs;

    public string Type => type;

    public int Runs => _runs;

    public bool ManagesTimeoutOverride { get; set; }

    public bool ManagesTimeout => ManagesTimeoutOverride;

    public Func<StepExecutionContext, StepDefinition, CancellationToken, Task<StepExecutionResult>>? Behavior { get; set; }

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runs);
        if (Behavior is not null)
        {
            return await Behavior(context, step, cancellationToken);
        }

        if (!string.IsNullOrEmpty(step.SaveAs))
        {
            context.Variables[step.SaveAs] = "value-of-" + step.Id;
        }

        return Ok(step);
    }

    public static StepExecutionResult Ok(StepDefinition step) => new()
    {
        Id = step.Id,
        Type = step.Type,
        Name = step.Name ?? step.Id,
        Assertions = [],
        Passed = true,
    };
}
