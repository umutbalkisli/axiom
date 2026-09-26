using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Axiom.Models;

namespace Axiom.Runtime;

public sealed class DbQueryExecutor : IDbQueryExecutor, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<CachedConnection>> _connections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDbConnectionFactory> _factories;

    public DbQueryExecutor(IEnumerable<IDbConnectionFactory> factories)
    {
        _factories = factories.ToDictionary(f => f.Provider, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(DbConnectionDefinition connectionDefinition, string sql, CancellationToken cancellationToken)
    {
        var provider = connectionDefinition.Provider.Trim();
        var connection = GetOrCreate(provider, connectionDefinition.ConnectionString);

        await connection.Gate.WaitAsync(cancellationToken);
        try
        {
            if (connection.DbConnection.State != ConnectionState.Open)
            {
                await connection.DbConnection.OpenAsync(cancellationToken);
            }

            await using var command = connection.DbConnection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await ReadAllRowsAsync(reader, cancellationToken);
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            await connection.Value.DisposeAsync();
        }

        _connections.Clear();
    }

    private CachedConnection GetOrCreate(string provider, string connectionString)
    {
        if (!_factories.TryGetValue(provider, out var factory))
        {
            throw new InvalidOperationException($"Unsupported DB provider '{provider}'");
        }

        var key = $"{factory.Provider}\0{connectionString}";
        return _connections.GetOrAdd(
            key,
            static (_, state) => new Lazy<CachedConnection>(() => new CachedConnection(state.factory.Create(state.connectionString))),
            (factory, connectionString)).Value;
    }

    private static async Task<List<Dictionary<string, object?>>> ReadAllRowsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}