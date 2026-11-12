using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Domain.Entities;

public class Invoice : Entity
{
    public Guid CustomerId { get; private init; }
    public Customer Customer { get; private init; } = null!;
    public string Number { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private init; } = null!;
    public decimal PaidAmount { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Optimistic concurrency token: two payments can't update the same invoice unnoticed.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public decimal OpenAmount => Amount - PaidAmount;

    private Invoice() { }

    public Invoice(Guid customerId, string number, DateOnly issueDate, DateOnly dueDate, decimal amount, string currency, DateTimeOffset createdAt)
    {
        CustomerId = customerId;
        Currency = Common.Currency.Ensure(currency);
        Status = InvoiceStatus.Open;
        CreatedAt = createdAt;
        SetDetails(number, issueDate, dueDate, amount);
    }

    /// <summary>Details can only change before any payment is recorded.</summary>
    public void Update(string number, DateOnly issueDate, DateOnly dueDate, decimal amount)
    {
        if (Status != InvoiceStatus.Open || PaidAmount != 0)
        {
            throw new DomainException($"Invoice {Number} can't be changed because it is {Status}.");
        }

        SetDetails(number, issueDate, dueDate, amount);
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Payment amount must be positive.");
        }

        if (Status is InvoiceStatus.Cancelled or InvoiceStatus.Paid)
        {
            throw new DomainException($"Invoice {Number} is {Status} and can't receive payments.");
        }

        if (amount > OpenAmount)
        {
            throw new DomainException($"Payment {amount} exceeds the open amount {OpenAmount} of invoice {Number}.");
        }

        PaidAmount += amount;
        RefreshStatus();
    }

    /// <summary>Undoes a previously applied payment, e.g. when a match is removed.</summary>
    public void ReversePayment(decimal amount)
    {
        if (amount <= 0 || amount > PaidAmount)
        {
            throw new DomainException($"Can't reverse {amount} on invoice {Number}; paid amount is {PaidAmount}.");
        }

        PaidAmount -= amount;
        RefreshStatus();
    }

    public void Cancel()
    {
        if (PaidAmount != 0)
        {
            throw new DomainException($"Invoice {Number} has payments and can't be cancelled.");
        }

        Status = InvoiceStatus.Cancelled;
    }

    private void SetDetails(string number, DateOnly issueDate, DateOnly dueDate, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new DomainException("Invoice number is required.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Invoice amount must be positive.");
        }

        if (dueDate < issueDate)
        {
            throw new DomainException("Due date can't be before issue date.");
        }

        Number = number.Trim();
        IssueDate = issueDate;
        DueDate = dueDate;
        Amount = amount;
    }

    private void RefreshStatus() =>
        Status = PaidAmount == 0 ? InvoiceStatus.Open
            : PaidAmount < Amount ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Paid;
}
