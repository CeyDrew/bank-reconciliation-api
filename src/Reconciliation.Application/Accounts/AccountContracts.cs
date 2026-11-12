using FluentValidation;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Accounts;

public record AccountResponse(Guid Id, string Name, string Iban, string Currency, bool IsActive, DateTimeOffset CreatedAt)
{
    public static AccountResponse From(Account account) => new(
        account.Id, account.Name, account.Iban.Value, account.Currency, account.IsActive, account.CreatedAt);
}

public record CreateAccountRequest(string Name, string Iban, string Currency);

/// <summary>IBAN and currency are fixed once transactions may exist; only name and status change.</summary>
public record UpdateAccountRequest(string Name, bool IsActive);

public class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Iban).NotEmpty().ValidIban();
        RuleFor(x => x.Currency).NotEmpty().ValidCurrency();
    }
}

public class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
