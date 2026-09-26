using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Axiom.Runtime;

public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public string Provider => "sqlite";

    public DbConnection Create(string connectionString) => new SqliteConnection(connectionString);
}

public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    public string Provider => "sqlserver";

    public DbConnection Create(string connectionString) => new SqlConnection(connectionString);
}
