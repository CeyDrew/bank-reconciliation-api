using Reconciliation.Domain.Common;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Domain.Entities;

/// <summary>A company that receives invoices and pays them by bank transfer.</summary>
public class Customer : Entity
{
    public string Name { get; private set; } = null!;
    public TaxNumber TaxNumber { get; private set; } = null!;

    /// <summary>The customer's own bank account; used to recognise their payments.</summary>
    public Iban? Iban { get; private set; }

    public string? Email { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private Customer() { }

    public Customer(string name, TaxNumber taxNumber, Iban? iban, string? email, DateTimeOffset createdAt)
    {
        Update(name, taxNumber, iban, email);
        CreatedAt = createdAt;
    }

    public void Update(string name, TaxNumber taxNumber, Iban? iban, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Customer name is required.");
        }

        Name = name.Trim();
        TaxNumber = taxNumber;
        Iban = iban;
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }
}
