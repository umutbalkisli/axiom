using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Axiom.Models;

namespace Axiom.Runtime;

/// <summary>
/// Runs SQL through the registered <see cref="IDbConnectionFactory"/> providers. Connections are pooled per
/// connection string for the duration of a run: parallel tests query on connections of their own, and a
/// connection is reused once its query is done.
/// </summary>
public sealed class DbQueryExecutor : IDbQueryExecutor, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<ConnectionPool>> _pools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IDbConnectionFactory> _factories;

    /// <summary>
    /// Creates an executor that supports the providers of <paramref name="factories"/>.
    /// </summary>
    public DbQueryExecutor(IEnumerable<IDbConnectionFactory> factories)
    {
        _factories = factories.ToDictionary(f => f.Provider, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs <paramref name="sql"/> on the connection described by <paramref name="connectionDefinition"/> and returns every row.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> QueryAsync(DbConnectionDefinition connectionDefinition, string sql, CancellationToken cancellationToken)
    {
        var pool = GetPool(connectionDefinition.Provider.Trim(), connectionDefinition.ConnectionString);
        var connection = await pool.RentAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await ReadAllRowsAsync(reader, cancellationToken);
        }
        finally
        {
            pool.Return(connection);
        }
    }

    /// <summary>
    /// Closes every connection opened during the run.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var pool in _pools.Values)
        {
            if (pool.IsValueCreated)
            {
                await pool.Value.DisposeAsync();
            }
        }

        _pools.Clear();
    }

    private ConnectionPool GetPool(string provider, string connectionString)
    {
        if (!_factories.TryGetValue(provider, out var factory))
        {
            throw new InvalidOperationException($"Unsupported DB provider '{provider}'");
        }

        var key = $"{factory.Provider}\0{connectionString}";
        return _pools.GetOrAdd(
            key,
            static (_, state) => new Lazy<ConnectionPool>(() => new ConnectionPool(state.factory, state.connectionString)),
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

    /// <summary>
    /// The open connections of one connection string. A caller rents an idle connection, or a new one when all are
    /// busy, and returns it after its query. A database that cannot be shared across connections gets exactly one,
    /// which callers take turns on.
    /// </summary>
    private sealed class ConnectionPool(IDbConnectionFactory factory, string connectionString) : IAsyncDisposable
    {
        private readonly ConcurrentBag<DbConnection> _idle = [];
        private readonly ConcurrentBag<DbConnection> _all = [];
        private readonly SemaphoreSlim? _turn = factory.SupportsParallelConnections(connectionString) ? null : new SemaphoreSlim(1, 1);

        public async Task<DbConnection> RentAsync(CancellationToken cancellationToken)
        {
            if (_turn is not null)
            {
                await _turn.WaitAsync(cancellationToken);
            }

            try
            {
                if (!_idle.TryTake(out var connection))
                {
                    connection = factory.Create(connectionString);
                    _all.Add(connection);
                }

                if (connection.State == ConnectionState.Broken)
                {
                    // Left broken by an earlier query (lost link, interrupted command): reopen it.
                    await connection.CloseAsync();
                }

                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken);
                }

                return connection;
            }
            catch
            {
                _turn?.Release();
                throw;
            }
        }

        public void Return(DbConnection connection)
        {
            _idle.Add(connection);
            _turn?.Release();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in _all)
            {
                await connection.DisposeAsync();
            }

            _turn?.Dispose();
        }
    }
}
