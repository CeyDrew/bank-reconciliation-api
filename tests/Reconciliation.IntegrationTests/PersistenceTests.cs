using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Domain.Entities;
using Reconciliation.IntegrationTests.Infrastructure;

namespace Reconciliation.IntegrationTests;

/// <summary>Database guarantees that the in-memory provider used by unit tests can't prove.</summary>
[Collection(ContainersCollection.Name)]
public class PersistenceTests(ContainersFixture containers) : IntegrationTest(containers)
{
    [Fact]
    public async Task Concurrent_payments_on_the_same_invoice_are_detected_by_rowversion()
    {
        await using var firstScope = App.CreateScope(out var first);
        await using var secondScope = App.CreateScope(out var second);
        var a = await first.Invoices.SingleAsync(i => i.Number == "INV-2026-0001");
        var b = await second.Invoices.SingleAsync(i => i.Number == "INV-2026-0001");

        a.ApplyPayment(10_000m);
        await first.SaveChangesAsync();

        b.ApplyPayment(10_000m); // would overpay the invoice if both saves succeeded
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Same_bank_transaction_cannot_be_stored_twice()
    {
        await using var scope = App.CreateScope(out var db);
        var account = await db.Accounts.FirstAsync();
        var now = DateTimeOffset.UtcNow;

        db.BankTransactions.Add(new BankTransaction(account.Id, "BANK-42", DateOnly.FromDateTime(now.UtcDateTime), 100m, "TRY", "x", null, null, now));
        await db.SaveChangesAsync();
        db.BankTransactions.Add(new BankTransaction(account.Id, "BANK-42", DateOnly.FromDateTime(now.UtcDateTime), 100m, "TRY", "x", null, null, now));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Microsoft.Data.SqlClient.SqlException>()
            .Which.Number.Should().Be(2601); // duplicate key in unique index
    }

    [Fact]
    public async Task Seed_and_migrations_produce_the_demo_data()
    {
        await using var scope = App.CreateScope(out var db);

        (await db.Accounts.CountAsync()).Should().Be(2);
        (await db.Customers.CountAsync()).Should().Be(4);
        (await db.Invoices.CountAsync()).Should().Be(8);
        (await db.Users.Select(u => u.Email).ToListAsync()).Should().BeEquivalentTo([TestApp.AdminEmail, TestApp.AccountantEmail]);
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }
}
