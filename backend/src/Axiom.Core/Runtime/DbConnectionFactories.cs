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
