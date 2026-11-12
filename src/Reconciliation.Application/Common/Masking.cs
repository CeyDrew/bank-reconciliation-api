using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Application.Common;

/// <summary>Hides account numbers from users who don't need them (anyone but an admin).</summary>
public static class Masking
{
    public static string? Iban(Iban? iban, bool reveal) => iban is null ? null : reveal ? iban.Value : iban.Masked;

    /// <summary>For raw, unvalidated values coming from the bank.</summary>
    public static string? Iban(string? value, bool reveal) =>
        value is null || reveal ? value
        : value.Length <= 8 ? new string('*', value.Length)
        : value[..4] + new string('*', value.Length - 8) + value[^4..];
}
