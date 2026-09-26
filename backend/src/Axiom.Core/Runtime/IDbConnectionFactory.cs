using System.Data.Common;

namespace Axiom.Runtime;

/// <summary>
/// Creates connections for one database <c>provider</c>. Register an implementation to support another database.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// The <c>provider</c> value in a collection's connection definition (case-insensitive).
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Creates a connection for <paramref name="connectionString"/>.
    /// </summary>
    DbConnection Create(string connectionString);

    /// <summary>
    /// True when several connections with <paramref name="connectionString"/> see the same data, so parallel tests
    /// may query it at the same time on different connections. False for a database that lives inside a single
    /// connection (such as an in-memory SQLite database); its queries then take turns on one connection.
    /// </summary>
    bool SupportsParallelConnections(string connectionString) => true;
}
