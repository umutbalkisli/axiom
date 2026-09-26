using System.Diagnostics;
using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Creates the executor.
/// </summary>
/// <summary>
/// Runs a <c>db_query</c> step: executes SQL and checks assertions on the rows.
/// </summary>
public sealed class DbQueryStepExecutor(IDbQueryExecutor dbQueryExecutor, AssertionEngine assertionEngine) : IStepExecutor
{
    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public const string StepType = "db_query";

    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    public string Type => StepType;

    /// <summary>
    /// Runs the query and evaluates the step's assertions on <c>row_count</c>, <c>rows</c> and <c>duration_ms</c>.
    /// </summary>
    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionContext context, StepDefinition step, CancellationToken cancellationToken)
    {
        var variables = context.Variables;

        if (string.IsNullOrWhiteSpace(step.Connection))
        {
            return StepResults.Error(step, "db_query step requires connection");
        }

        if (string.IsNullOrWhiteSpace(step.Sql))
        {
            return StepResults.Error(step, "db_query step requires sql");
        }

        if (!context.Collection.Connections.TryGetValue(step.Connection, out var connectionDefinition))
        {
            return StepResults.Error(step, $"Connection '{step.Connection}' was not found");
        }

        var sql = TemplateResolver.ResolveString(step.Sql, variables);
        // Recorded before running, so it is there to look at even when the query fails.
        variables[$"{step.Id}_sql"] = sql;
        var resolvedConnection = new DbConnectionDefinition
        {
            Provider = connectionDefinition.Provider,
            ConnectionString = context.Secrets.Expand(connectionDefinition.ConnectionString),
        };
        var watch = Stopwatch.StartNew();
        var rows = await dbQueryExecutor.QueryAsync(resolvedConnection, sql, cancellationToken);
        watch.Stop();

        variables[$"{step.Id}_rows"] = rows;
        variables[$"{step.Id}_row_count"] = rows.Count;
        if (!string.IsNullOrWhiteSpace(step.SaveAs))
        {
            variables[step.SaveAs] = rows;
        }

        var assertionResults = assertionEngine.EvaluateAll(step, variables, source => source switch
        {
            "row_count" => rows.Count,
            "duration_ms" => watch.Elapsed.TotalMilliseconds,
            "rows" => rows,
            _ => variables.TryGetValue(source, out var value) ? value : null,
        });

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name ?? step.Id,
            Assertions = assertionResults,
            Passed = assertionResults.All(a => a.Passed),
            DurationMs = watch.Elapsed.TotalMilliseconds,
            RowCount = rows.Count,
        };
    }
}
