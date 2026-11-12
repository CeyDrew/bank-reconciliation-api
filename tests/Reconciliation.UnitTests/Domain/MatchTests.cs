using FluentAssertions;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.UnitTests.Domain;

public class MatchTests
{
    [Fact]
    public void Full_match_updates_transaction_and_invoice()
    {
        var transaction = TestData.Transaction(1_000m);
        var invoice = TestData.Invoice(1_000m);

        var match = Match.Create(transaction, invoice, 1_000m, 95, "Invoice number in description", MatchMethod.Automatic, null, TestData.Now);

        match.TransactionId.Should().Be(transaction.Id);
        match.InvoiceId.Should().Be(invoice.Id);
        transaction.Status.Should().Be(TransactionStatus.Matched);
        transaction.UnmatchedAmount.Should().Be(0m);
        invoice.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void One_payment_can_cover_two_invoices()
    {
        var transaction = TestData.Transaction(1_500m);
        var first = TestData.Invoice(1_000m);
        var second = TestData.Invoice(500m);

        Match.Create(transaction, first, 1_000m, 80, "Customer IBAN", MatchMethod.Automatic, null, TestData.Now);
        transaction.Status.Should().Be(TransactionStatus.PartiallyMatched);

        Match.Create(transaction, second, 500m, 80, "Customer IBAN", MatchMethod.Automatic, null, TestData.Now);

        transaction.Status.Should().Be(TransactionStatus.Matched);
        first.Status.Should().Be(InvoiceStatus.Paid);
        second.Status.Should().Be(InvoiceStatus.Paid);
    }

    [Fact]
    public void Overpayment_leaves_rest_of_transaction_unmatched()
    {
        var transaction = TestData.Transaction(1_200m);
        var invoice = TestData.Invoice(1_000m);

        Match.Create(transaction, invoice, invoice.OpenAmount, 90, "Invoice number in description", MatchMethod.Automatic, null, TestData.Now);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        transaction.Status.Should().Be(TransactionStatus.PartiallyMatched);
        transaction.UnmatchedAmount.Should().Be(200m);
    }

    [Fact]
    public void Currency_mismatch_is_rejected()
    {
        var transaction = TestData.Transaction(100m, "EUR");
        var invoice = TestData.Invoice(100m);

        var act = () => Match.Create(transaction, invoice, 100m, 100, "Manual", MatchMethod.Manual, "user", TestData.Now);

        act.Should().Throw<DomainException>();
        transaction.MatchedAmount.Should().Be(0m);
    }

    [Fact]
    public void Outgoing_transaction_cannot_be_matched()
    {
        var transaction = TestData.Transaction(-100m);
        var invoice = TestData.Invoice(100m);

        var act = () => Match.Create(transaction, invoice, 100m, 100, "Manual", MatchMethod.Manual, "user", TestData.Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Ignored_transaction_cannot_be_matched()
    {
        var transaction = TestData.Transaction(100m);
        transaction.Ignore();

        var act = () => Match.Create(transaction, TestData.Invoice(100m), 100m, 100, "Manual", MatchMethod.Manual, "user", TestData.Now);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Score_outside_range_is_rejected(int score)
    {
        var act = () => Match.Create(TestData.Transaction(), TestData.Invoice(), 100m, score, "x", MatchMethod.Automatic, null, TestData.Now);

        act.Should().Throw<DomainException>();
    }
}
