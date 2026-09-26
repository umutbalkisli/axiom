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
}
