using Reconciliation.Domain.Common;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Domain.Entities;

/// <summary>One of our own bank accounts whose transactions are imported and reconciled.</summary>
public class Account : Entity
{
    public string Name { get; private set; } = null!;
    public Iban Iban { get; private init; } = null!;
    public string Currency { get; private init; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private Account() { }

    public Account(string name, Iban iban, string currency, DateTimeOffset createdAt)
    {
        Rename(name);
        Iban = iban;
        Currency = Common.Currency.Ensure(currency);
        IsActive = true;
        CreatedAt = createdAt;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Account name is required.");
        }

        Name = name.Trim();
    }

    public void Activate() => IsActive = true;

    /// <summary>Inactive accounts are skipped by the transaction import job.</summary>
    public void Deactivate() => IsActive = false;
}
