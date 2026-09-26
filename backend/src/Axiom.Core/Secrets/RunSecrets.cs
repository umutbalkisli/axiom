using System.Text.RegularExpressions;
using Axiom.Runtime;

namespace Axiom.Secrets;

/// <summary>The secret values resolved for one run, with template expansion and output masking.</summary>
public sealed partial class RunSecrets
{
    /// <summary>Values shorter than this are not masked, so short values do not shred unrelated text in reports.</summary>
    private const int MinMaskedLength = 4;
    private const string MaskText = "********";

    public static RunSecrets Empty { get; } = new(new Dictionary<string, string>());

    private readonly Dictionary<string, string> _values;
    private readonly string[] _maskedValues;

    public RunSecrets(IReadOnlyDictionary<string, string> values)
    {
        _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        _maskedValues = _values.Values
            .Where(v => v.Length >= MinMaskedLength)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(v => v.Length)
            .ToArray();
    }

    public bool IsEmpty => _values.Count == 0;

    /// <summary>Exposes the values to templates as <c>{{secret.name}}</c>.</summary>
    public IReadOnlyDictionary<string, object?> AsTemplateVariables() =>
        new Dictionary<string, object?>(_values.ToDictionary(p => p.Key, p => (object?)p.Value), StringComparer.OrdinalIgnoreCase);

    /// <summary>Replaces <c>{{secret.name}}</c> tokens only; other templates are left for the step to resolve.</summary>
    public string Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return SecretToken().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            return _values.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException($"Secret '{name}' is not declared in the collection's secrets section.");
        });
    }

    public string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var value in _maskedValues)
        {
            text = text.Replace(value, MaskText, StringComparison.Ordinal);
        }

        return text;
    }

    public StepExecutionResult Mask(StepExecutionResult step)
    {
        if (_maskedValues.Length == 0)
        {
            return step;
        }

        return new StepExecutionResult
        {
            Id = step.Id,
            Type = step.Type,
            Name = step.Name,
            Assertions = step.Assertions.Select(Mask).ToList(),
            Passed = step.Passed,
            Error = Mask(step.Error),
            StatusCode = step.StatusCode,
            DurationMs = step.DurationMs,
            RowCount = step.RowCount,
        };
    }

    private AssertionResult Mask(AssertionResult assertion) => new()
    {
        Source = assertion.Source,
        Operator = assertion.Operator,
        Expected = MaskValue(assertion.Expected),
        Actual = MaskValue(assertion.Actual),
        Passed = assertion.Passed,
        Error = Mask(assertion.Error),
    };

    private object? MaskValue(object? value)
    {
        switch (value)
        {
            case string text:
                return Mask(text);
            case null or bool or int or long or double or decimal:
                return value;
            default:
                // Structured values (JSON nodes, rows): fall back to masked text when they contain a secret.
                var rendered = value.ToString() ?? string.Empty;
                var masked = Mask(rendered);
                return masked == rendered ? value : masked;
        }
    }

    [GeneratedRegex(@"\{\{\s*secret\.([A-Za-z_][A-Za-z0-9_\-]*)\s*\}\}")]
    private static partial Regex SecretToken();
}
