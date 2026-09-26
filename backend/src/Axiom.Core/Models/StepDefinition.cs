namespace Axiom.Models;

/// <summary>
/// One step of a test: an HTTP request, a database query, or an include of shared steps.
/// </summary>
public sealed class StepDefinition
{
    /// <summary>
    /// Identifier of the step, unique within its test; values it produces are named after it (<c>{id}_status</c>).
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// The kind of step: <c>request</c>, <c>db_query</c> or <c>include</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>
    /// Display name of the step; defaults to the id.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// HTTP method (request steps).
    /// </summary>
    public string? Method { get; set; }
    /// <summary>
    /// Request URL; may contain <c>{{variables}}</c> (request steps).
    /// </summary>
    public string? Url { get; set; }
    /// <summary>
    /// Query string parameters added to the URL (request steps).
    /// </summary>
    public Dictionary<string, string> QueryParams { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Request headers (request steps).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Request body text (request steps).
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Name of the collection connection to query (db_query steps).
    /// </summary>
    public string? Connection { get; set; }
    /// <summary>
    /// The SQL text to run (db_query steps).
    /// </summary>
    public string? Sql { get; set; }
    /// <summary>
    /// Name under which the step's result is saved for later steps.
    /// </summary>
    public string? SaveAs { get; set; }

    /// <summary>
    /// For <c>include</c> steps: the id (file name without suffix) of the shared steps to run.
    /// </summary>
    public string? Ref { get; set; }

    /// <summary>
    /// The assertions to check after the step ran.
    /// </summary>
    public List<AssertionDefinition> Assert { get; set; } = [];
}