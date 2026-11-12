using System.Net.Http.Json;
using Reconciliation.Application.Abstractions;

namespace Reconciliation.Infrastructure.Banking;

public class BankApiOptions
{
    public const string SectionName = "FakeBank";

    public string BaseUrl { get; init; } = "";
}

/// <summary>
/// HTTP client for the FakeBank statement API. Retries, timeouts and the circuit breaker
/// are added around it by the resilience pipeline registered in DependencyInjection.
/// </summary>
public class FakeBankClient(HttpClient http) : IBankClient
{
    private const int PageSize = 100;

    public async Task<BankStatementPage> GetTransactionsAsync(string iban, DateOnly from, int page, CancellationToken ct)
    {
        var url = $"api/v1/accounts/{Uri.EscapeDataString(iban)}/transactions?from={from:yyyy-MM-dd}&page={page}&pageSize={PageSize}";
        var response = await http.GetFromJsonAsync<StatementPageDto>(url, ct)
            ?? throw new InvalidOperationException("Bank returned an empty response.");

        var lines = response.Items
            .Select(i => new BankStatementLine(i.TransactionId, i.BookingDate, i.Amount, i.Currency, i.Description,
                i.CounterpartyName, i.CounterpartyIban))
            .ToList();
        return new BankStatementPage(lines, response.HasMore);
    }

    // Wire format of the bank; kept separate from the application's model on purpose.
    private record StatementPageDto(List<StatementLineDto> Items, bool HasMore);

    private record StatementLineDto(
        string TransactionId,
        DateOnly BookingDate,
        decimal Amount,
        string Currency,
        string? Description,
        string? CounterpartyName,
        string? CounterpartyIban);
}
