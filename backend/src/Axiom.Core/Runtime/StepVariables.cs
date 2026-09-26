using Axiom.Models;
using Axiom.Secrets;

namespace Axiom.Runtime;

/// <summary>
/// Builds the variables a run of steps starts with.
/// </summary>
public static class StepVariables
{
    /// <summary>
    /// Reserved variable name under which secrets are exposed to templates.
    /// </summary>
    public const string SecretName = "secret";

    /// <summary>
    /// The variables a run of steps starts with: collection variables, then <paramref name="extra"/> (test variables), with secrets expanded.
    /// </summary>
    public static Dictionary<string, object?> Initial(CollectionDefinition collection, IEnumerable<KeyValuePair<string, object?>> extra, RunSecrets secrets)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in collection.Variables.Concat(extra))
        {
            // Variables may embed {{secret.name}}; expand them here so every later use sees the real value.
            variables[pair.Key] = pair.Value is string text ? secrets.Expand(text) : pair.Value;
        }

        variables[SecretName] = secrets.AsTemplateVariables();
        return variables;
    }
}
