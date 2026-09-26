namespace Axiom.Models;

public sealed class DbConnectionDefinition
{
    public string Provider { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
}