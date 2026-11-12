using System.Text.RegularExpressions;

namespace Reconciliation.Application.Matching;

/// <summary>Helpers for the free text that payers type into a transfer.</summary>
internal static partial class TextMatching
{
    private static readonly HashSet<string> LegalSuffixes =
        ["LTD", "STI", "AS", "INC", "CO", "LLC", "CORP", "GMBH", "SAN", "TIC", "VE"];

    /// <summary>Upper-case ASCII letters and digits only: "inv-2026/0008" becomes "INV20260008".</summary>
    public static string Normalize(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : string.Concat(text.Where(char.IsLetterOrDigit).Select(Fold));

    /// <summary>
    /// Upper-cases and maps Turkish letters to ASCII, as banks usually do in statements.
    /// ToUpperInvariant alone keeps "ı" as is, so "Gıda" would never equal "GIDA".
    /// </summary>
    private static char Fold(char c) => c switch
    {
        'ı' or 'i' or 'İ' => 'I',
        'ş' or 'Ş' => 'S',
        'ğ' or 'Ğ' => 'G',
        'ü' or 'Ü' => 'U',
        'ö' or 'Ö' => 'O',
        'ç' or 'Ç' => 'C',
        _ => char.ToUpperInvariant(c)
    };

    public static string DigitsOnly(string text) => string.Concat(text.Where(char.IsAsciiDigit));

    /// <summary>
    /// True when <paramref name="token"/> occurs in <paramref name="haystack"/> without being part of a longer
    /// number, so "INV20260001" is not found in "INV202600012".
    /// </summary>
    public static bool ContainsToken(string haystack, string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        for (var index = haystack.IndexOf(token, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(token, index + 1, StringComparison.Ordinal))
        {
            var end = index + token.Length;
            var digitBefore = index > 0 && char.IsAsciiDigit(token[0]) && char.IsAsciiDigit(haystack[index - 1]);
            var digitAfter = end < haystack.Length && char.IsAsciiDigit(haystack[end]);
            if (!digitBefore && !digitAfter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Stand-alone 10-digit numbers, the shape of a Turkish tax number.</summary>
    public static IEnumerable<string> TenDigitNumbers(string? text) =>
        string.IsNullOrEmpty(text) ? [] : TenDigits().Matches(text).Select(m => m.Value);

    /// <summary>
    /// Company names match when their words, ignoring punctuation and legal forms, are the same:
    /// "ACME TEXTILES LTD" and "Acme Textiles Ltd." are the same company.
    /// </summary>
    public static bool SameCompanyName(string? a, string? b)
    {
        var left = NameWords(a);
        var right = NameWords(b);
        return left.Count > 0 && left.SetEquals(right);
    }

    private static HashSet<string> NameWords(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? []
            : NonWord().Split(string.Concat(name.Select(Fold)))
                .Where(word => word.Length > 1 && !LegalSuffixes.Contains(word)) // single letters: "A.Ş." splits into A and Ş
                .ToHashSet();

    [GeneratedRegex(@"(?<!\d)\d{10}(?!\d)")]
    private static partial Regex TenDigits();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
