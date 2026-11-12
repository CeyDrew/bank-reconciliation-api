using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Domain.Entities;

/// <summary>Links (part of) a bank transaction to (part of) an invoice.</summary>
public class Match : Entity
{
    public Guid TransactionId { get; private init; }
    public BankTransaction Transaction { get; private init; } = null!;
    public Guid InvoiceId { get; private init; }
    public Invoice Invoice { get; private init; } = null!;
    public decimal Amount { get; private init; }

    /// <summary>Confidence of the matching engine, 0-100. Manual matches are 100.</summary>
    public int Score { get; private init; }

    /// <summary>Human-readable explanation, e.g. "Invoice number found in description; amount equal".</summary>
    public string Reason { get; private init; } = null!;

    public MatchMethod Method { get; private init; }
    public string? CreatedBy { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private Match() { }

    private Match(BankTransaction transaction, Invoice invoice, decimal amount, int score, string reason,
        MatchMethod method, string? createdBy, DateTimeOffset createdAt)
    {
        Transaction = transaction;
        TransactionId = transaction.Id;
        Invoice = invoice;
        InvoiceId = invoice.Id;
        Amount = amount;
        Score = score;
        Reason = reason;
        Method = method;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Creates a match and updates both sides, so the transaction's matched amount
    /// and the invoice's paid amount always agree with the matches.
    /// </summary>
    public static Match Create(BankTransaction transaction, Invoice invoice, decimal amount, int score, string reason,
        MatchMethod method, string? createdBy, DateTimeOffset createdAt)
    {
        if (transaction.Currency != invoice.Currency)
        {
            throw new DomainException($"Currency mismatch: transaction {transaction.Currency}, invoice {invoice.Currency}.");
        }

        if (score is < 0 or > 100)
        {
            throw new DomainException("Match score must be between 0 and 100.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Match reason is required.");
        }

        transaction.ApplyMatch(amount);
        invoice.ApplyPayment(amount);

        return new Match(transaction, invoice, amount, score, reason.Trim(), method, createdBy, createdAt);
    }
}
