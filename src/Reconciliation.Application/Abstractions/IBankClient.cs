namespace Reconciliation.Application.Abstractions;

/// <summary>Reads account statements from the bank. Implemented in Infrastructure over HTTP.</summary>
public interface IBankClient
{
    /// <summary>Returns movements booked on or after <paramref name="from"/>, oldest first, one page at a time.</summary>
    Task<BankStatementPage> GetTransactionsAsync(string iban, DateOnly from, int page, CancellationToken ct);
}

public record BankStatementLine(
    string TransactionId,
    DateOnly BookingDate,
    decimal Amount,
    string Currency,
    string? Description,
    string? CounterpartyName,
    string? CounterpartyIban);

public record BankStatementPage(IReadOnlyList<BankStatementLine> Items, bool HasMore);
