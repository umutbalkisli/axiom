using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Axiom.Runtime;

/// <summary>
/// Creates SQLite connections (provider <c>sqlite</c>).
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    /// <summary>
    /// The provider name: <c>sqlite</c>.
    /// </summary>
    public string Provider => "sqlite";

    /// <summary>
    /// Creates a SQLite connection.
    /// </summary>
    public DbConnection Create(string connectionString) => new SqliteConnection(connectionString);

    /// <summary>
    /// False for a private in-memory database: every connection to <c>:memory:</c> is a separate, empty database.
    /// A shared-cache in-memory database (<c>Mode=Memory;Cache=Shared</c>) is shared, like a file.
    /// </summary>
    public bool SupportsParallelConnections(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var inMemory = builder.Mode == SqliteOpenMode.Memory || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
        return !inMemory || builder.Cache == SqliteCacheMode.Shared;
    }
}

/// <summary>
/// Creates SQL Server connections (provider <c>sqlserver</c>).
/// </summary>
public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    /// <summary>
    /// The provider name: <c>sqlserver</c>.
    /// </summary>
    public string Provider => "sqlserver";

    /// <summary>
    /// Creates a SQL Server connection.
    /// </summary>
    public DbConnection Create(string connectionString) => new SqlConnection(connectionString);
}
