namespace Axiom.Models;

public sealed class StepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Name { get; set; }

    public string? Method { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string> QueryParams { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Body { get; set; }

    public string? Connection { get; set; }
    public string? Sql { get; set; }
    public string? SaveAs { get; set; }

    /// <summary>For <c>include</c> steps: the id (file name without suffix) of the shared steps to run.</summary>
    public string? Ref { get; set; }

    public List<AssertionDefinition> Assert { get; set; } = [];
}