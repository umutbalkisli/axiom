using System.Reflection;
using System.Text.RegularExpressions;
using Axiom.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Axiom.Serialization;

/// <summary>
/// Turns YamlDotNet exceptions into messages a test author can act on: file, line and column, and for an unknown
/// key the section it was found in, the closest valid key and the list of valid keys.
/// </summary>
public static partial class YamlErrors
{
    // The sections of a collection's files, as users know them.
    private static readonly Dictionary<string, (string Label, Type Type)> Sections = new[]
    {
        ("the collection", typeof(CollectionDefinition)),
        ("a connection", typeof(DbConnectionDefinition)),
        ("run_settings", typeof(RunSettings)),
        ("request_defaults", typeof(RequestDefaults)),
        ("a secret", typeof(SecretReference)),
        ("a secret environment", typeof(SecretSource)),
        ("the test", typeof(TestCaseDefinition)),
        ("the shared steps", typeof(SharedStepsDefinition)),
        ("a step", typeof(StepDefinition)),
        ("an assertion", typeof(AssertionDefinition)),
    }.ToDictionary(item => item.Item2.FullName!, item => item, StringComparer.Ordinal);

    /// <summary>
    /// Describes <paramref name="exception"/>, raised while reading <paramref name="filePath"/>.
    /// </summary>
    public static string Describe(string filePath, YamlException exception)
    {
        // YamlDotNet wraps the real cause; the innermost YAML error has the most precise message and position.
        var innermost = exception;
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is YamlException yaml)
            {
                innermost = yaml;
            }
        }

        var location = $"{filePath} (line {innermost.Start.Line}, column {innermost.Start.Column})";
        var unknown = UnknownProperty().Match(innermost.Message);
        if (!unknown.Success)
        {
            return $"{location}: {innermost.Message}";
        }

        var key = unknown.Groups["key"].Value;
        if (!Sections.TryGetValue(unknown.Groups["type"].Value, out var section))
        {
            return $"{location}: unknown key '{key}'.";
        }

        var known = KnownKeys(section.Type);
        var suggestion = Closest(key, known);
        var hint = suggestion is null ? string.Empty : $" Did you mean '{suggestion}'?";
        return $"{location}: unknown key '{key}' in {section.Label}.{hint} Valid keys: {string.Join(", ", known)}.";
    }

    /// <summary>
    /// The YAML keys that <paramref name="type"/> accepts.
    /// </summary>
    internal static IReadOnlyList<string> KnownKeys(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<YamlIgnoreAttribute>() is null)
            .Select(p => YamlSerialization.KeyOf(p.Name))
            .ToList();

    /// <summary>
    /// The known key nearest to <paramref name="key"/>, when it is close enough to be a likely typo.
    /// </summary>
    internal static string? Closest(string key, IEnumerable<string> known)
    {
        var normalized = key.Trim().ToLowerInvariant().Replace('-', '_');
        var best = known
            .Select(candidate => (Candidate: candidate, Distance: Distance(normalized, candidate)))
            .OrderBy(pair => pair.Distance)
            .FirstOrDefault();
        return best.Candidate is not null && best.Distance <= Math.Max(2, normalized.Length / 3) ? best.Candidate : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }

    [GeneratedRegex(@"Property '(?<key>[^']*)' not found on type '(?<type>[^']*)'")]
    private static partial Regex UnknownProperty();
}
