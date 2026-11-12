using FluentValidation;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Customers;

public record CustomerResponse(Guid Id, string Name, string TaxNumber, string? Iban, string? Email, DateTimeOffset CreatedAt)
{
    public static CustomerResponse From(Customer customer, bool revealIban) => new(
        customer.Id, customer.Name, customer.TaxNumber.Value, Masking.Iban(customer.Iban, revealIban), customer.Email, customer.CreatedAt);
}

public record CustomerQuery : PageRequest
{
    /// <summary>Part of the name, or an exact tax number.</summary>
    public string? Search { get; init; }
}

public record SaveCustomerRequest(string Name, string TaxNumber, string? Iban, string? Email);

public class SaveCustomerRequestValidator : AbstractValidator<SaveCustomerRequest>
{
    public SaveCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxNumber).NotEmpty().ValidTaxNumber();
        RuleFor(x => x.Iban).ValidIban().When(x => !string.IsNullOrWhiteSpace(x.Iban));
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
