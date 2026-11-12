using FluentValidation;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Invoices;

public record InvoiceResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string Number,
    DateOnly IssueDate,
    DateOnly DueDate,
    decimal Amount,
    decimal PaidAmount,
    decimal OpenAmount,
    string Currency,
    InvoiceStatus Status,
    DateTimeOffset CreatedAt)
{
    /// <summary>Requires <see cref="Invoice.Customer"/> to be loaded.</summary>
    public static InvoiceResponse From(Invoice invoice) => new(
        invoice.Id, invoice.CustomerId, invoice.Customer.Name, invoice.Number, invoice.IssueDate, invoice.DueDate,
        invoice.Amount, invoice.PaidAmount, invoice.OpenAmount, invoice.Currency, invoice.Status, invoice.CreatedAt);
}

public record InvoiceQuery : PageRequest
{
    public Guid? CustomerId { get; init; }
    public InvoiceStatus? Status { get; init; }
}

public record CreateInvoiceRequest(Guid CustomerId, string Number, DateOnly IssueDate, DateOnly DueDate, decimal Amount, string Currency);

public record UpdateInvoiceRequest(string Number, DateOnly IssueDate, DateOnly DueDate, decimal Amount);

public class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Number).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.IssueDate);
        RuleFor(x => x.Amount).ValidMoney();
        RuleFor(x => x.Currency).NotEmpty().ValidCurrency();
    }
}

public class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.Number).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DueDate).GreaterThanOrEqualTo(x => x.IssueDate);
        RuleFor(x => x.Amount).ValidMoney();
    }
}
