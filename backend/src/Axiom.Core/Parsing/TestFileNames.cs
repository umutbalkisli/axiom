using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Axiom.Parsing;

/// <summary>
/// Derives test file names from test names. A file name is a short, readable, filesystem-safe slug;
/// the test's real name lives inside the file, so the file name never has to carry it in full.
/// </summary>
public static partial class TestFileNames
{
    /// <summary>
    /// The longest file name (without suffix) that is generated.
    /// </summary>
    public const int MaxLength = 48;
    private const string Fallback = "untitled";

    /// <summary>
    /// "Şifre Değiştir / Get todo #1" becomes "sifre-degistir-get-todo-1": ASCII, lowercase, at most <see cref="MaxLength"/> characters.
    /// </summary>
    public static string Slug(string? text)
    {
        var source = (text ?? string.Empty)
            .Trim()
            .Replace(CollectionPaths.TestFileSuffix, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(CollectionPaths.SharedFileSuffix, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace('ı', 'i')
            .Replace('İ', 'I');

        var builder = new StringBuilder(source.Length);
        var pendingDash = false;
        foreach (var ch in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop the accent, keep the letter
            }

            if (char.IsAsciiLetterOrDigit(ch) || ch == '_')
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingDash = true;
            }
        }

        return Truncate(builder.ToString());
    }

    /// <summary>
    /// True when <paramref name="fileId"/> is what Axiom would have generated for <paramref name="name"/>
    /// (optionally with a "-2" style suffix), meaning nobody has given the file a custom name.
    /// </summary>
    public static bool IsGeneratedFrom(string fileId, string? name)
    {
        var slug = Slug(name);
        return string.Equals(fileId, slug, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fileId, $"^{Regex.Escape(slug)}-\\d+$", RegexOptions.IgnoreCase);
    }

    private static string Truncate(string slug)
    {
        if (slug.Length > MaxLength)
        {
            var cut = slug[..MaxLength];
            var lastDash = cut.LastIndexOf('-');
            // Prefer ending on a whole word, unless that throws away most of the name.
            slug = lastDash >= MaxLength / 2 ? cut[..lastDash] : cut;
        }

        slug = slug.Trim('-');
        return slug.Length == 0 ? Fallback : slug;
    }
}
