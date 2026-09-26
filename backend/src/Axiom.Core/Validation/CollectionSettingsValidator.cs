namespace Axiom.Validation;

public static class CollectionSettingsValidator
{
    public static List<FieldError> ValidateVariables(Dictionary<string, object?> variables) =>
        ValidateKeys(variables.Keys, "variables", "Variable key cannot be empty.");

    public static List<FieldError> ValidateConnections(Dictionary<string, object?> connections) =>
        ValidateKeys(connections.Keys, "connections", "Connection name cannot be empty.");

    private static List<FieldError> ValidateKeys(IEnumerable<string> keys, string field, string message) =>
        keys.Where(string.IsNullOrWhiteSpace).Select(_ => new FieldError(field, message)).ToList();
}
