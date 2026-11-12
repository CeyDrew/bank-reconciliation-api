using Microsoft.EntityFrameworkCore;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Infrastructure.Persistence;

/// <summary>
/// Demo data for local development. All companies, IBANs and tax numbers are fictional:
/// IBANs use the unassigned bank codes 99990-99994 and only pass the checksum.
/// </summary>
internal static class DataSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(DbContext context, CancellationToken ct)
    {
        if (await context.Set<Account>().AnyAsync(ct))
        {
            return;
        }

        context.Set<Account>().AddRange(
            new Account("Main TRY Account", Iban.Create("TR549999000000001000000001"), Currency.TurkishLira, SeededAt),
            new Account("Collections TRY Account", Iban.Create("TR279999000000001000000002"), Currency.TurkishLira, SeededAt));

        var acme = Customer("Acme Textiles Ltd.", "1234567890", "TR719999100000002000000011", "billing@acme.example");
        var globex = Customer("Globex Logistics Inc.", "2345678909", "TR099999200000002000000022", "accounts@globex.example");
        var initech = Customer("Initech Software Ltd.", "3456789014", "TR449999300000002000000033", "finance@initech.example");
        var hooli = Customer("Hooli Foods Co.", "4567890128", null, "ap@hooli.example");
        context.Set<Customer>().AddRange(acme, globex, initech, hooli);

        context.Set<Invoice>().AddRange(
            Invoice(acme, "INV-2026-0001", new DateOnly(2026, 10, 1), 12_500.00m),
            Invoice(acme, "INV-2026-0002", new DateOnly(2026, 10, 15), 8_750.50m),
            Invoice(globex, "INV-2026-0003", new DateOnly(2026, 10, 5), 42_000.00m),
            Invoice(globex, "INV-2026-0004", new DateOnly(2026, 10, 20), 3_200.00m),
            // Two invoices with the same amount: the matching engine must not guess between them.
            Invoice(initech, "INV-2026-0005", new DateOnly(2026, 10, 10), 15_000.00m),
            Invoice(initech, "INV-2026-0006", new DateOnly(2026, 10, 25), 15_000.00m),
            Invoice(hooli, "INV-2026-0007", new DateOnly(2026, 10, 12), 9_999.99m),
            Invoice(hooli, "INV-2026-0008", new DateOnly(2026, 11, 2), 27_350.00m));

        await context.SaveChangesAsync(ct);
    }

    private static Customer Customer(string name, string taxNumber, string? iban, string email) =>
        new(name, TaxNumber.Create(taxNumber), iban is null ? null : Iban.Create(iban), email, SeededAt);

    private static Invoice Invoice(Customer customer, string number, DateOnly issueDate, decimal amount) =>
        new(customer.Id, number, issueDate, issueDate.AddDays(30), amount, Currency.TurkishLira, SeededAt);
}
