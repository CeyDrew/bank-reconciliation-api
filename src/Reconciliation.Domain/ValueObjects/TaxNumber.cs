using System.Diagnostics.CodeAnalysis;
using Reconciliation.Domain.Common;

namespace Reconciliation.Domain.ValueObjects;

/// <summary>Turkish tax identification number (VKN): 10 digits, the last one is a check digit.</summary>
public sealed record TaxNumber
{
    public string Value { get; }

    private TaxNumber(string value) => Value = value;

    public static TaxNumber Create(string input) =>
        TryCreate(input, out var taxNumber) ? taxNumber : throw new DomainException($"'{input}' is not a valid tax number.");

    public static bool TryCreate(string? input, [NotNullWhen(true)] out TaxNumber? taxNumber)
    {
        taxNumber = null;
        var value = input?.Trim();
        if (value is not { Length: 10 } || !value.All(char.IsAsciiDigit) || CheckDigit(value) != value[9] - '0')
        {
            return false;
        }

        taxNumber = new TaxNumber(value);
        return true;
    }

    public static bool IsValid(string? input) => TryCreate(input, out _);

    public override string ToString() => Value;

    /// <summary>Check digit for the first nine digits, using the algorithm of the Turkish Revenue Administration.</summary>
    public static int CheckDigit(string digits)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var shifted = (digits[i] - '0' + 9 - i) % 10;
            var weighted = shifted * (1 << (9 - i)) % 9;
            if (shifted != 0 && weighted == 0)
            {
                weighted = 9;
            }

            sum += weighted;
        }

        return (10 - sum % 10) % 10;
    }
}
