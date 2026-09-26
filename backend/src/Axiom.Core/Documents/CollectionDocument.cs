using Axiom.Models;

namespace Axiom.Documents;

public sealed class CollectionDocument
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SecretReference> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public RunSettingsDocument RunSettings { get; set; } = new();
    public RequestDefaultsDocument RequestDefaults { get; set; } = new();
}