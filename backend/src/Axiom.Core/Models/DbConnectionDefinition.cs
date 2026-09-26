namespace Axiom.Models;

/// <summary>
/// A named database connection of a collection.
/// </summary>
public sealed class DbConnectionDefinition
{
    /// <summary>
    /// The database provider, such as <c>sqlite</c> or <c>sqlserver</c>.
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    /// <summary>
    /// The provider's connection string; may contain <c>{{secret.name}}</c>.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}