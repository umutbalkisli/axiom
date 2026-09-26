using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Axiom.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Axiom.Runtime;

public sealed class DbQueryExecutor : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<CachedConnection>> _connections = new(StringComparer.Ordinal);

    public async Task<List<Dictionary<string, object?>>> QueryAsync(DbConnectionDefinition connectionDefinition, string sql, CancellationToken cancellationToken)
    {
        var provider = connectionDefinition.Provider.Trim().ToLowerInvariant();
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
        var key = $"{provider}\0{connectionString}";
        return _connections.GetOrAdd(
            key,
            static (_, state) => new Lazy<CachedConnection>(() => new CachedConnection(CreateConnection(state.provider, state.connectionString))),
            (provider, connectionString)).Value;
    }

    private static DbConnection CreateConnection(string provider, string connectionString)
    {
        return provider switch
        {
            "sqlite" => new SqliteConnection(connectionString),
            "sqlserver" => new SqlConnection(connectionString),
            _ => throw new InvalidOperationException($"Unsupported DB provider '{provider}'"),
        };
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