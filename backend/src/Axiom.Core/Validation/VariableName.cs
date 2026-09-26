using System.Text.RegularExpressions;

namespace Axiom.Validation;

/// <summary>Naming rule for variables and saved results: letters and underscores only.</summary>
public static partial class VariableName
{
    /// <summary>Exposed to templates as <c>{{secret.name}}</c>, so a variable cannot take this name.</summary>
    private const string Reserved = "secret";

    /// <summary>Returns a readable problem description, or null when <paramref name="name"/> is a valid variable name.</summary>
    public static string? Check(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Variable name cannot be empty.";
        }

        if (!Pattern().IsMatch(name))
        {
            return $"Variable name '{name}' is invalid: use only letters (A-Z, a-z) and underscores.";
        }

        return string.Equals(name, Reserved, StringComparison.OrdinalIgnoreCase)
            ? $"Variable name '{name}' is reserved for secrets."
            : null;
    }

    [GeneratedRegex("^[A-Za-z_]+$")]
    private static partial Regex Pattern();
}
