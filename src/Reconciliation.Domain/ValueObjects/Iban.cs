using System.Diagnostics.CodeAnalysis;
using Reconciliation.Domain.Common;

namespace Reconciliation.Domain.ValueObjects;

/// <summary>
/// International Bank Account Number, validated with the ISO 13616 mod-97 check.
/// Stored without spaces, upper-case.
/// </summary>
public sealed record Iban
{
    private const int TurkishIbanLength = 26;

    public string Value { get; }

    private Iban(string value) => Value = value;

    public static Iban Create(string input) =>
        TryCreate(input, out var iban) ? iban : throw new DomainException($"'{input}' is not a valid IBAN.");

    public static bool TryCreate(string? input, [NotNullWhen(true)] out Iban? iban)
    {
        iban = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = Normalize(input);
        if (!HasValidFormat(value) || Mod97(value[4..] + value[..4]) != 1)
        {
            return false;
        }

        iban = new Iban(value);
        return true;
    }

    public static bool IsValid(string? input) => TryCreate(input, out _);

    /// <summary>Builds an IBAN from a country code and BBAN by calculating the two check digits.</summary>
    public static Iban FromBban(string countryCode, string bban)
    {
        var country = countryCode.ToUpperInvariant();
        var normalizedBban = Normalize(bban);
        var checkDigits = 98 - Mod97(normalizedBban + country + "00");
        return Create($"{country}{checkDigits:00}{normalizedBban}");
    }

    /// <summary>Shows only the country code, check digits and last four characters.</summary>
    public string Masked => Value[..4] + new string('*', Value.Length - 8) + Value[^4..];

    public override string ToString() => Value;

    private static string Normalize(string input) =>
        string.Concat(input.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    private static bool HasValidFormat(string value)
    {
        if (value.Length is < 15 or > 34)
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1]) ||
            !char.IsAsciiDigit(value[2]) || !char.IsAsciiDigit(value[3]))
        {
            return false;
        }

        if (value.StartsWith("TR", StringComparison.Ordinal) && value.Length != TurkishIbanLength)
        {
            return false;
        }

        return value.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));
    }

    /// <summary>Remainder of the number formed by replacing letters with 10..35, computed digit by digit.</summary>
    private static int Mod97(string input)
    {
        var remainder = 0;
        foreach (var c in input)
        {
            remainder = char.IsAsciiDigit(c)
                ? (remainder * 10 + (c - '0')) % 97
                : (remainder * 100 + (c - 'A' + 10)) % 97;
        }

        return remainder;
    }
}
