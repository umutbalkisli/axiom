using System.Text.RegularExpressions;
using Axiom.Models;

namespace Axiom.Validation;

/// <summary>
/// Validates variables, connections and secret references before they are saved.
/// </summary>
public static partial class CollectionSettingsValidator
{
    /// <summary>
    /// Checks variable names.
    /// </summary>
    public static List<FieldError> ValidateVariables(Dictionary<string, object?> variables) =>
        variables.Keys
            .Select(VariableName.Check)
            .OfType<string>()
            .Select(problem => new FieldError("variables", problem))
            .ToList();

    /// <summary>
    /// Checks that every connection has a name.
    /// </summary>
    public static List<FieldError> ValidateConnections(Dictionary<string, object?> connections) =>
        ValidateKeys(connections.Keys, "connections", "Connection name cannot be empty.");

    /// <summary>
    /// Checks secret names, providers, keys and environment overrides.
    /// </summary>
    public static List<FieldError> ValidateSecrets(Dictionary<string, SecretReference> secrets)
    {
        var errors = new List<FieldError>();
        foreach (var (name, reference) in secrets)
        {
            if (!SecretName().IsMatch(name))
            {
                errors.Add(new FieldError("secrets", $"Secret name '{name}' must start with a letter or underscore and contain only letters, digits, '_' or '-'."));
            }

            ValidateSource(reference, $"secrets.{name}", errors);
            foreach (var (environment, source) in reference?.Environments ?? [])
            {
                if (string.IsNullOrWhiteSpace(environment))
                {
                    errors.Add(new FieldError($"secrets.{name}.environments", "Environment name cannot be empty."));
                }

                ValidateSource(source, $"secrets.{name}.environments.{environment}", errors);
            }
        }

        return errors;
    }

    private static void ValidateSource(SecretSource? source, string prefix, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(source?.Provider))
        {
            errors.Add(new FieldError($"{prefix}.provider", "Secret provider is required."));
        }

        if (string.IsNullOrWhiteSpace(source?.Key))
        {
            errors.Add(new FieldError($"{prefix}.key", "Secret key is required."));
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_\\-]*$")]
    private static partial Regex SecretName();

    private static List<FieldError> ValidateKeys(IEnumerable<string> keys, string field, string message) =>
        keys.Where(string.IsNullOrWhiteSpace).Select(_ => new FieldError(field, message)).ToList();
}
