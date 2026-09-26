namespace Axiom.Models;

public sealed class CollectionDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, DbConnectionDefinition> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SecretReference> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public RunSettings RunSettings { get; set; } = new();
    public RequestDefaults RequestDefaults { get; set; } = new();
}
