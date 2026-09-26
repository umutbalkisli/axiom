using Axiom.Models;

namespace Axiom.Runtime;

public interface IDbQueryExecutor
{
    Task<List<Dictionary<string, object?>>> QueryAsync(DbConnectionDefinition connectionDefinition, string sql, CancellationToken cancellationToken);
}
