using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Domain.Entities;

/// <summary>A movement on one of our bank accounts, as reported by the bank.</summary>
public class BankTransaction : Entity
{
    public Guid AccountId { get; private init; }
    public Account Account { get; private init; } = null!;

    /// <summary>The bank's own identifier. Unique per account, so the same movement is never imported twice.</summary>
    public string ExternalId { get; private init; } = null!;

    public DateOnly BookingDate { get; private init; }

    /// <summary>Positive for incoming money, negative for outgoing.</summary>
    public decimal Amount { get; private init; }

    public string Currency { get; private init; } = null!;
    public string Description { get; private init; } = null!;
    public string? CounterpartyName { get; private init; }

    /// <summary>Raw value from the bank; not validated because bank data can be incomplete.</summary>
    public string? CounterpartyIban { get; private init; }

    public decimal MatchedAmount { get; private set; }
    public TransactionStatus Status { get; private set; }

    /// <summary>
    /// Set when a person reverses a match on this transaction, so the matching engine
    /// doesn't re-apply the match the person just rejected.
    /// </summary>
    public bool RequiresManualMatching { get; private set; }
    public DateTimeOffset ImportedAt { get; private init; }

    public bool IsIncoming => Amount > 0;
    public decimal UnmatchedAmount => Amount - MatchedAmount;

    private BankTransaction() { }

    public BankTransaction(
        Guid accountId, string externalId, DateOnly bookingDate, decimal amount, string currency,
        string? description, string? counterpartyName, string? counterpartyIban, DateTimeOffset importedAt)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new DomainException("External transaction id is required.");
        }

        if (amount == 0)
        {
            throw new DomainException("Transaction amount can't be zero.");
        }

        AccountId = accountId;
        ExternalId = externalId.Trim();
        BookingDate = bookingDate;
        Amount = amount;
        Currency = Common.Currency.Ensure(currency);
        Description = description?.Trim() ?? string.Empty;
        CounterpartyName = counterpartyName?.Trim();
        CounterpartyIban = counterpartyIban?.Trim();
        Status = TransactionStatus.Unmatched;
        ImportedAt = importedAt;
    }

    public void ApplyMatch(decimal amount)
    {
        if (!IsIncoming)
        {
            throw new DomainException("Only incoming transactions can be matched to invoices.");
        }

        if (Status == TransactionStatus.Ignored)
        {
            throw new DomainException("Ignored transactions can't be matched.");
        }

        if (amount <= 0 || amount > UnmatchedAmount)
        {
            throw new DomainException($"Match amount {amount} must be positive and at most {UnmatchedAmount}.");
        }

        MatchedAmount += amount;
        RefreshStatus();
    }

    public void ReverseMatch(decimal amount)
    {
        if (amount <= 0 || amount > MatchedAmount)
        {
            throw new DomainException($"Can't reverse {amount}; matched amount is {MatchedAmount}.");
        }

        MatchedAmount -= amount;
        RefreshStatus();
    }

    /// <summary>Marks a transaction that needs no reconciliation (e.g. bank fees, transfers between own accounts).</summary>
    public void Ignore()
    {
        if (MatchedAmount != 0)
        {
            throw new DomainException("A transaction with matches can't be ignored.");
        }

        Status = TransactionStatus.Ignored;
    }

    public void ExcludeFromAutomaticMatching() => RequiresManualMatching = true;

    /// <summary>Puts an ignored transaction back into reconciliation.</summary>
    public void Restore()
    {
        if (Status != TransactionStatus.Ignored)
        {
            throw new DomainException("Only ignored transactions can be restored.");
        }

        Status = TransactionStatus.Unmatched;
    }

    private void RefreshStatus() =>
        Status = MatchedAmount == 0 ? TransactionStatus.Unmatched
            : MatchedAmount < Amount ? TransactionStatus.PartiallyMatched
            : TransactionStatus.Matched;
}
