using Axiom.Parsing;
using Axiom.Runtime;
using Axiom.Services;
using Axiom.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axiom;

public static class AxiomServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Axiom engine. Extend it by registering more <see cref="IStepExecutor"/>,
    /// <see cref="IStepValidator"/>, <see cref="IAssertionOperator"/> or <see cref="IDbConnectionFactory"/> services.
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

        services.TryAddSingleton<AssertionEngine>();

        // Steps: one executor (runtime) and one validator (authoring) per step type.
        services.AddSingleton<IStepValidator, RequestStepValidator>();
        services.AddSingleton<IStepValidator, DbQueryStepValidator>();
        services.TryAddSingleton<TestCaseValidator>();

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
