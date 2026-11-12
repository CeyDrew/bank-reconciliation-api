using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Reconciliation.IntegrationTests.Infrastructure;

namespace Reconciliation.IntegrationTests;

/// <summary>
/// The full business flow against real SQL Server and Redis:
/// import from the bank, automatic matching, report, ERP webhooks, manual matching.
/// </summary>
[Collection(ContainersCollection.Name)]
public class EndToEndTests(ContainersFixture containers) : IntegrationTest(containers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Bank_to_erp_flow()
    {
        var admin = await App.AsAdmin();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Readiness covers SQL Server and Redis.
        (await App.Anonymous().GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        // 1. Import: the 11 scripted movements land on the main account.
        var import = await PostAsync<ImportDto>(admin, "/api/v1/transactions/import");
        import.Should().BeEquivalentTo(new ImportDto(Accounts: 2, FailedAccounts: 0, Fetched: 11, Imported: 11, Duplicates: 0, Rejected: 0));

        // 2. Automatic matching: 7 of the 9 incoming payments.
        var run = await PostAsync<RunDto>(admin, "/api/v1/reconciliation/run");
        run.Should().BeEquivalentTo(new RunDto(Processed: 9, Matched: 7, Unmatched: 2, MatchedAmount: 103_800.49m));

        // 3. Report: what came in, what is matched, what is left to explain.
        var report = await admin.GetFromJsonAsync<ReportDto>(
            $"/api/v1/reports/reconciliation?from={today.AddDays(-10):yyyy-MM-dd}&to={today:yyyy-MM-dd}", Json);
        report!.Days.Should().HaveCount(11);
        report.Totals.Should().BeEquivalentTo(new
        {
            IncomingCount = 9,
            IncomingAmount = 120_335.05m,
            MatchedAmount = 103_800.49m,
            UnmatchedCount = 3,              // ambiguous 15,000, unknown payer 1,234.56, overpaid 300
            UnmatchedAmount = 16_534.56m,
            OutgoingCount = 2,
            OutgoingAmount = 2_512.50m,
            MatchRatePercent = 86.3m
        });
        report.OpenInvoices.Should().BeEquivalentTo(new { Count = 2, OpenAmount = 30_000m });

        // 4. ERP: every match becomes a signed webhook that the fake ERP verifies and books.
        var dispatch = await PostAsync<DispatchDto>(admin, "/api/v1/webhooks/dispatch");
        dispatch.Should().Be(new DispatchDto(Delivered: 7, Retrying: 0, Failed: 0));
        var erp = await App.FakeBank.CreateClient().GetFromJsonAsync<ErpDto>("/erp/webhooks", Json);
        erp!.Received.Should().Be(7);

        // 5. Manual matching closes the ambiguous payment.
        var accountant = await App.AsAccountant();
        var transaction = (await accountant.GetFromJsonAsync<Page<IdDto>>("/api/v1/transactions?status=Unmatched&incoming=true&pageSize=100", Json))!
            .Items.Single(t => t.ExternalId == "FB-S005");
        var invoice = (await accountant.GetFromJsonAsync<Page<InvoiceDto>>("/api/v1/invoices?pageSize=100", Json))!
            .Items.Single(i => i.Number == "INV-2026-0005");

        var manual = await accountant.PostAsJsonAsync($"/api/v1/transactions/{transaction.Id}/matches",
            new { allocations = new[] { new { invoiceId = invoice.Id, amount = 15_000m } }, note = "Confirmed by email" });
        manual.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await accountant.GetFromJsonAsync<ReportDto>(
            $"/api/v1/reports/reconciliation?from={today.AddDays(-10):yyyy-MM-dd}&to={today:yyyy-MM-dd}", Json);
        after!.Totals.UnmatchedAmount.Should().Be(1_534.56m);
        (await PostAsync<DispatchDto>(admin, "/api/v1/webhooks/dispatch")).Delivered.Should().Be(1);
    }

    [Fact]
    public async Task Parallel_imports_store_each_movement_once()
    {
        var admin = await App.AsAdmin();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.PostAsync("/api/v1/transactions/import", null)));

        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK)
            .And.OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        var page = await admin.GetFromJsonAsync<Page<IdDto>>("/api/v1/transactions?pageSize=100", Json);
        page!.TotalCount.Should().Be(11);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url)
    {
        var response = await client.PostAsync(url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private record ImportDto(int Accounts, int FailedAccounts, int Fetched, int Imported, int Duplicates, int Rejected);
    private record RunDto(int Processed, int Matched, int Unmatched, decimal MatchedAmount);
    private record DispatchDto(int Delivered, int Retrying, int Failed);
    private record ErpDto(int Received, int DuplicatesIgnored);
    private record TotalsDto(int IncomingCount, decimal IncomingAmount, decimal MatchedAmount, int UnmatchedCount,
        decimal UnmatchedAmount, int OutgoingCount, decimal OutgoingAmount, decimal MatchRatePercent);
    private record OpenInvoicesDto(int Count, decimal OpenAmount, int OverdueCount, decimal OverdueAmount);
    private record ReportDto(TotalsDto Totals, OpenInvoicesDto OpenInvoices, List<JsonElement> Days);
    private record Page<T>(List<T> Items, int TotalCount);
    private record IdDto(Guid Id, string ExternalId);
    private record InvoiceDto(Guid Id, string Number);
}
