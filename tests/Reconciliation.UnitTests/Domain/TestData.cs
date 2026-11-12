using Reconciliation.Domain.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.UnitTests.Domain;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    public static Invoice Invoice(decimal amount = 1_000m, string currency = Currency.TurkishLira) =>
        new(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), amount, currency, Now);

    public static BankTransaction Transaction(decimal amount = 1_000m, string currency = Currency.TurkishLira) =>
        new(Guid.NewGuid(), "BANK-1", new DateOnly(2026, 10, 15), amount, currency, "Payment INV-1", "Acme", null, Now);
}
