namespace Reconciliation.FakeBank.Statements;

/// <summary>
/// Hand-written movements on the API's seeded main account. Each one exercises a matching rule
/// against the seeded invoices (INV-2026-0001..0008). Booking dates are relative to today, but the
/// transaction ids are fixed, so a restarted bank never produces duplicates.
/// </summary>
internal static class ScenarioCatalog
{
    public const string MainAccountIban = "TR549999000000001000000001";

    private const string Acme = "TR719999100000002000000011";
    private const string Globex = "TR099999200000002000000022";
    private const string Initech = "TR449999300000002000000033";

    // Not stored on the customer in the API, so the IBAN alone can't identify the payer.
    private static readonly string HooliUnknownIban = FakeIban.FromAccountNumber(5, 4242);

    private record Scenario(string Id, int DaysAgo, decimal Amount, string Description, string? Name, string? Iban);

    private static readonly Scenario[] Scenarios =
    [
        // Exact: invoice number in the description, full amount, known IBAN.
        new("FB-S001", 10, 12_500.00m, "Payment for INV-2026-0001", "ACME TEXTILES LTD", Acme),
        // Partial payment, paid off later by FB-S011.
        new("FB-S002", 9, 5_000.00m, "INV-2026-0002 partial payment", "ACME TEXTILES LTD", Acme),
        // Invoice number written differently.
        new("FB-S003", 9, 42_000.00m, "Invoice 2026 0003 settlement", "GLOBEX LOGISTICS INC", Globex),
        // Overpayment: invoice is 3,200.00.
        new("FB-S004", 8, 3_500.00m, "INV-2026-0004", "GLOBEX LOGISTICS INC", Globex),
        // No reference; two open invoices of exactly this amount. Must not be guessed.
        new("FB-S005", 7, 15_000.00m, "Transfer", "INITECH SOFTWARE LTD", Initech),
        // Customer without a known IBAN; tax number in the description.
        new("FB-S006", 6, 9_999.99m, "VKN 4567890128 payment", "HOOLI FOODS CO", HooliUnknownIban),
        // Lower case, no dashes.
        new("FB-S007", 5, 27_350.00m, "inv2026-0008", "HOOLI FOODS CO", null),
        // Unknown payer: stays unmatched.
        new("FB-S008", 4, 1_234.56m, "Transfer", "JOHN DOE", null),
        // Outgoing: rent and bank fee are never matched to invoices.
        new("FB-S009", 3, -2_500.00m, "Office rent November", "BLUE RIVER REAL ESTATE", null),
        new("FB-S010", 3, -12.50m, "EFT fee", null, null),
        // Rest of INV-2026-0002 (8,750.50 - 5,000.00).
        new("FB-S011", 2, 3_750.50m, "INV-2026-0002 remaining", "ACME TEXTILES LTD", Acme),
    ];

    public static IEnumerable<StatementLine> For(string iban, DateOnly today) =>
        iban != MainAccountIban
            ? []
            : Scenarios.Select(s => new StatementLine(
                s.Id, today.AddDays(-s.DaysAgo), s.Amount, "TRY", s.Description, s.Name, s.Iban));
}
