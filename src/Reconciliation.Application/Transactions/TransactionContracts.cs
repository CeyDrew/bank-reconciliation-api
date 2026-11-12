using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Transactions;

public record TransactionResponse(
    Guid Id,
    Guid AccountId,
    string ExternalId,
    DateOnly BookingDate,
    decimal Amount,
    decimal MatchedAmount,
    decimal UnmatchedAmount,
    string Currency,
    string Description,
    string? CounterpartyName,
    string? CounterpartyIban,
    TransactionStatus Status,
    DateTimeOffset ImportedAt)
{
    public static TransactionResponse From(BankTransaction t) => new(
        t.Id, t.AccountId, t.ExternalId, t.BookingDate, t.Amount, t.MatchedAmount, t.UnmatchedAmount, t.Currency,
        t.Description, t.CounterpartyName, t.CounterpartyIban, t.Status, t.ImportedAt);
}

public record TransactionQuery : PageRequest
{
    public Guid? AccountId { get; init; }
    public TransactionStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
