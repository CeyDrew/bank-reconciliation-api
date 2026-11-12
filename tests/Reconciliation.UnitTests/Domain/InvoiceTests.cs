using FluentAssertions;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.UnitTests.Domain;

public class InvoiceTests
{
    [Fact]
    public void New_invoice_is_open_with_full_open_amount()
    {
        var invoice = TestData.Invoice(1_000m);

        invoice.Status.Should().Be(InvoiceStatus.Open);
        invoice.OpenAmount.Should().Be(1_000m);
    }

    [Fact]
    public void Partial_payment_makes_invoice_partially_paid()
    {
        var invoice = TestData.Invoice(1_000m);

        invoice.ApplyPayment(400m);

        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.PaidAmount.Should().Be(400m);
        invoice.OpenAmount.Should().Be(600m);
    }

    [Fact]
    public void Payments_adding_up_to_amount_make_invoice_paid()
    {
        var invoice = TestData.Invoice(1_000m);

        invoice.ApplyPayment(400m);
        invoice.ApplyPayment(600m);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.OpenAmount.Should().Be(0m);
    }

    [Fact]
    public void Payment_above_open_amount_is_rejected()
    {
        var invoice = TestData.Invoice(1_000m);
        invoice.ApplyPayment(800m);

        var act = () => invoice.ApplyPayment(200.01m);

        act.Should().Throw<DomainException>();
        invoice.PaidAmount.Should().Be(800m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_payment_is_rejected(decimal amount)
    {
        var act = () => TestData.Invoice().ApplyPayment(amount);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reversing_payment_reopens_invoice()
    {
        var invoice = TestData.Invoice(1_000m);
        invoice.ApplyPayment(1_000m);

        invoice.ReversePayment(1_000m);

        invoice.Status.Should().Be(InvoiceStatus.Open);
        invoice.PaidAmount.Should().Be(0m);
    }

    [Fact]
    public void Invoice_with_payments_cannot_be_cancelled_or_changed()
    {
        var invoice = TestData.Invoice(1_000m);
        invoice.ApplyPayment(100m);

        invoice.Invoking(i => i.Cancel()).Should().Throw<DomainException>();
        invoice.Invoking(i => i.Update("INV-2", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 500m))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancelled_invoice_cannot_receive_payments()
    {
        var invoice = TestData.Invoice();
        invoice.Cancel();

        invoice.Invoking(i => i.ApplyPayment(10m)).Should().Throw<DomainException>();
        invoice.Status.Should().Be(InvoiceStatus.Cancelled);
    }

    [Fact]
    public void Due_date_before_issue_date_is_rejected()
    {
        var act = () => new Reconciliation.Domain.Entities.Invoice(Guid.NewGuid(), "INV-1",
            new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), 100m, Currency.TurkishLira, TestData.Now);

        act.Should().Throw<DomainException>();
    }
}
