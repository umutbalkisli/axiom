using Axiom.Parsing;
using Axiom.Runtime;
using Axiom.Secrets;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axiom;

public static class AxiomServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Axiom engine. Extend it by registering more <see cref="IStepExecutor"/>,
    /// <see cref="IStepValidator"/>, <see cref="IAssertionOperator"/>, <see cref="IAssertionAggregation"/>, <see cref="IDbConnectionFactory"/> or <see cref="ISecretProvider"/> services.
    /// </summary>
    public static IServiceCollection AddAxiomCore(this IServiceCollection services)
    {
        services.TryAddSingleton<YamlCollectionLoader>();
        services.TryAddSingleton<CollectionRunner>();
        services.TryAddSingleton<CollectionManagementService>();
        services.TryAddSingleton<CollectionInitializer>();

        // Assertions
        foreach (var assertionOperator in BuiltInAssertionOperators.All)
        {
            services.AddSingleton(assertionOperator);
        }

        foreach (var aggregation in BuiltInAssertionAggregations.All)
        {
            services.AddSingleton(aggregation);
        }

        services.TryAddSingleton<AssertionEngine>();

        // Steps: one executor (runtime) and one validator (authoring) per step type.
        services.AddSingleton<IStepValidator, RequestStepValidator>();
        services.AddSingleton<IStepValidator, DbQueryStepValidator>();
        services.TryAddSingleton<TestCaseValidator>();

        // Secret providers. Connection settings for these come from the environment, not from collection files.
        services.AddSingleton<ISecretProvider, EnvSecretProvider>();
        services.AddSingleton<ISecretProvider, FileSecretProvider>();
        services.AddSingleton<ISecretProvider, KubernetesSecretProvider>();
        services.AddSingleton<ISecretProvider, VaultSecretProvider>();
        services.AddScoped<LocalSecretProvider>();
        services.AddScoped<ISecretProvider>(sp => sp.GetRequiredService<LocalSecretProvider>());
        services.AddScoped<SecretResolver>();

        // Per-run services (disposed with the run's scope).
        services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory, SqlServerConnectionFactory>();
        services.AddScoped<HttpClient>(_ => new HttpClient());
        services.AddScoped<IDbQueryExecutor, DbQueryExecutor>();
        services.AddScoped<IStepExecutor, RequestStepExecutor>();
        services.AddScoped<IStepExecutor, DbQueryStepExecutor>();
        services.AddScoped<TestCaseExecutor>();
        services.AddScoped<OpenApiImporter>();

        return services;
    }
}
