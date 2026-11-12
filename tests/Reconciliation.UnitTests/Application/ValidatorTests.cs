using FluentAssertions;
using Reconciliation.Application.Customers;
using Reconciliation.Application.Invoices;

namespace Reconciliation.UnitTests.Application;

public class ValidatorTests
{
    private readonly SaveCustomerRequestValidator _customerValidator = new();
    private readonly CreateInvoiceRequestValidator _invoiceValidator = new();

    [Fact]
    public void Valid_customer_passes()
    {
        var request = new SaveCustomerRequest("Acme", "1234567890", "TR719999100000002000000011", "a@acme.example");

        _customerValidator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Customer_without_iban_and_email_passes()
    {
        _customerValidator.Validate(new SaveCustomerRequest("Acme", "1234567890", null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Customer_with_bad_tax_number_and_iban_fails_on_both()
    {
        var result = _customerValidator.Validate(new SaveCustomerRequest("Acme", "1234567891", "TR000", null));

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["TaxNumber", "Iban"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.555)] // more than two decimals
    public void Invoice_with_invalid_amount_fails(decimal amount)
    {
        var request = new CreateInvoiceRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), amount, "TRY");

        _invoiceValidator.Validate(request).Errors.Should().ContainSingle(e => e.PropertyName == "Amount");
    }

    [Fact]
    public void Invoice_due_before_issue_fails()
    {
        var request = new CreateInvoiceRequest(Guid.NewGuid(), "INV-1", new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), 10m, "TRY");

        _invoiceValidator.Validate(request).Errors.Should().ContainSingle(e => e.PropertyName == "DueDate");
    }
}
