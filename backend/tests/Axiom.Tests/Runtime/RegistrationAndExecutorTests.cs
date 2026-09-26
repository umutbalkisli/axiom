using Axiom.Secrets;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Axiom.Tests.Runtime;

public class RegistrationTests
{
    [Fact]
    public async Task AddAxiomCore_registers_every_service_and_extension_point()
    {
        await using var services = new ServiceCollection().AddAxiomCore().BuildServiceProvider(validateScopes: true);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        Assert.NotNull(provider.GetRequiredService<CollectionRunner>());
        Assert.NotNull(provider.GetRequiredService<CollectionManagementService>());
        Assert.NotNull(provider.GetRequiredService<CollectionInitializer>());
        Assert.NotNull(provider.GetRequiredService<OpenApiImporter>());
        Assert.NotNull(provider.GetRequiredService<TestCaseValidator>());
        Assert.NotNull(provider.GetRequiredService<AssertionEngine>());
        Assert.NotNull(provider.GetRequiredService<SecretResolver>());

        Assert.Equal(["db_query", "include", "request"], provider.GetServices<IStepExecutor>().Select(e => e.Type).Order());
        Assert.Equal(["db_query", "include", "request"], provider.GetServices<IStepValidator>().Select(v => v.StepType).Order());
        Assert.Equal(["sqlite", "sqlserver"], provider.GetServices<IDbConnectionFactory>().Select(f => f.Provider).Order());
        Assert.Equal(["env", "file", "k8s", "local", "vault"], provider.GetServices<ISecretProvider>().Select(p => p.Name).Order());
        Assert.Equal(BuiltInAssertionAggregations.All.Count, provider.GetServices<IAssertionAggregation>().Count());
        Assert.Equal(BuiltInAssertionOperators.All.Count, provider.GetServices<IAssertionOperator>().Count());
    }

    [Fact]
    public async Task Extra_registrations_add_step_types_operators_and_secret_providers()
    {
        var services = new ServiceCollection().AddAxiomCore();
        services.AddScoped<IStepExecutor>(_ => new ProbeExecutor("custom"));
        services.AddSingleton<ISecretProvider>(new NamedProvider("my-vault"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.Contains("custom", scope.ServiceProvider.GetServices<IStepExecutor>().Select(e => e.Type));
        Assert.Contains("my-vault", scope.ServiceProvider.GetRequiredService<SecretResolver>().Providers.Select(p => p.Name));
    }

    private sealed class NamedProvider(string name) : ISecretProvider
    {
        public string Name => name;

        public string KeyFormat => "-";

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}

public class TestCaseExecutorTests
{
    private static readonly TestCaseExecutor Executor = new(new StepRunner([new ProbeExecutor()]));

    private static TestCaseDefinition Test(Dictionary<string, object?>? variables = null, params StepDefinition[] steps) => new()
    {
        Name = "t", SourceFile = "t.test.yaml", Variables = variables ?? new(StringComparer.OrdinalIgnoreCase), Steps = steps.ToList(),
    };

    [Fact]
    public async Task Runs_the_steps_with_collection_and_test_variables_and_records_timing()
    {
        var collection = Build.Collection();
        collection.Variables["shared"] = "from-collection";
        collection.Variables["both"] = "collection";
        var probe = new ProbeExecutor();
        object? seenShared = null, seenBoth = null;
        probe.Behavior = (context, step, _) =>
        {
            seenShared = context.Variables["shared"];
            seenBoth = context.Variables["both"];
            return Task.FromResult(ProbeExecutor.Ok(step));
        };
        var executor = new TestCaseExecutor(new StepRunner([probe]));

        var result = await executor.ExecuteAsync(collection, Test(new(StringComparer.OrdinalIgnoreCase) { ["both"] = "test" }, new StepDefinition { Id = "a", Type = "probe" }),
            RunSecrets.Empty, SharedStepsLibrary.Empty, default);

        Assert.True(result.Passed);
        Assert.Equal("from-collection", seenShared);
        Assert.Equal("test", seenBoth);                        // a test variable overrides the collection's
        Assert.True(result.CompletedAt >= result.StartedAt);
        Assert.Equal("t.test.yaml", result.SourceFile);
    }

    [Fact]
    public async Task Variables_that_cannot_be_resolved_become_a_single_error_step_and_nothing_runs()
    {
        var collection = Build.Collection();
        collection.Variables["needs_secret"] = "{{secret.not_declared}}";

        var result = await Executor.ExecuteAsync(collection, Test(null, new StepDefinition { Id = "a", Type = "probe" }), RunSecrets.Empty, SharedStepsLibrary.Empty, default);

        var step = Assert.Single(result.Steps);
        Assert.Equal("variables", step.Id);
        Assert.Equal(RunOutcome.Error, result.Outcome);
        Assert.Contains("not_declared", step.Error);
    }

    [Fact]
    public async Task Secrets_are_available_to_templates_under_the_reserved_secret_name()
    {
        var secrets = new RunSecrets(new Dictionary<string, string> { ["token"] = "tok-value" });
        var probe = new ProbeExecutor();
        object? seen = null;
        probe.Behavior = (context, step, _) =>
        {
            seen = TemplateResolver.ResolveObject("secret.token", context.Variables);
            return Task.FromResult(ProbeExecutor.Ok(step));
        };

        await new TestCaseExecutor(new StepRunner([probe])).ExecuteAsync(Build.Collection(), Test(null, new StepDefinition { Id = "a", Type = "probe" }), secrets, SharedStepsLibrary.Empty, default);

        Assert.Equal("tok-value", seen);
    }
}
