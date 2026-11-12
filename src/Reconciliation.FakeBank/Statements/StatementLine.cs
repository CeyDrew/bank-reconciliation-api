namespace Reconciliation.FakeBank.Statements;

/// <summary>One account movement as the bank reports it. Amount is negative for outgoing money.</summary>
public record StatementLine(
    string TransactionId,
    DateOnly BookingDate,
    decimal Amount,
    string Currency,
    string Description,
    string? CounterpartyName,
    string? CounterpartyIban);

public record StatementPage(IReadOnlyList<StatementLine> Items, int Page, int PageSize, int TotalCount, bool HasMore);
