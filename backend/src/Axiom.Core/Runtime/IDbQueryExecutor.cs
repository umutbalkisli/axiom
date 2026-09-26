using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Runs SQL text against a configured connection.
/// </summary>
public interface IDbQueryExecutor
{
    /// <summary>
    /// Runs <paramref name="sql"/> and returns every row as a dictionary of column name to value.
    /// </summary>
    Task<List<Dictionary<string, object?>>> QueryAsync(DbConnectionDefinition connectionDefinition, string sql, CancellationToken cancellationToken);
}
