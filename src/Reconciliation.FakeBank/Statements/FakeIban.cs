namespace Reconciliation.FakeBank.Statements;

/// <summary>Builds checksum-valid but fictional Turkish IBANs (bank codes 9999x are not assigned).</summary>
internal static class FakeIban
{
    public static string FromAccountNumber(int bankDigit, long accountNumber)
    {
        var bban = $"9999{bankDigit}0{accountNumber:0000000000000000}";
        var checkDigits = 98 - Mod97(bban + "TR00");
        return $"TR{checkDigits:00}{bban}";
    }

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
