using FluentAssertions;
using Reconciliation.Application.Matching;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.UnitTests.Matching;

public class MatchingEngineTests
{
    private const int Threshold = 80;
    private const string AcmeIban = "TR719999100000002000000011";
    private const string GlobexIban = "TR099999200000002000000022";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly MatchingEngine _engine = new();
    private readonly Customer _acme = Customer("Acme Textiles Ltd.", "1234567890", AcmeIban);
    private readonly Customer _globex = Customer("Globex Logistics Inc.", "2345678909", GlobexIban);
    private readonly Customer _hooli = Customer("Hooli Foods Co.", "4567890128", null);
    private readonly List<Invoice> _invoices = [];

    private Customer[] Customers => [_acme, _globex, _hooli];

    // ---- Reference in the description ----

    [Fact]
    public void Invoice_number_full_amount_and_known_iban_scores_100()
    {
        var invoice = AddInvoice(_acme, "INV-2026-0001", 12_500m);

        var result = Evaluate(Incoming(12_500m, "Payment for INV-2026-0001", AcmeIban));

        result.IsConfident(Threshold).Should().BeTrue();
        result.Allocations.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            InvoiceId = invoice.Id,
            Amount = 12_500m,
            Score = 100,
            Reason = "invoice number INV-2026-0001 in description; payer identified by counterparty IBAN; amount equals open amount"
        });
    }

    [Theory]
    [InlineData("inv2026-0001")]
    [InlineData("INV 2026 0001")]
    [InlineData("ref:INV/2026/0001.")]
    public void Invoice_number_is_found_whatever_the_formatting(string description)
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        var result = Evaluate(Incoming(100m, description));

        result.Allocations.Should().ContainSingle().Which.Reason.Should().StartWith("invoice number INV-2026-0001");
    }

    [Fact]
    public void Digits_of_invoice_number_score_lower_than_the_full_number()
    {
        AddInvoice(_globex, "INV-2026-0003", 42_000m);

        var result = Evaluate(Incoming(42_000m, "Invoice 2026 0003 settlement", GlobexIban));

        result.Score.Should().Be(MatchScores.InvoiceDigitsInDescription + MatchScores.PayerByIban + MatchScores.AmountEqualsOpenAmount);
        result.IsConfident(Threshold).Should().BeTrue();
    }

    [Fact]
    public void Longer_number_containing_the_invoice_number_is_not_a_reference()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        var result = Evaluate(Incoming(100m, "INV-2026-00012"));

        result.HasProposal.Should().BeFalse();
    }

    [Fact]
    public void Partial_payment_allocates_the_whole_transaction()
    {
        var invoice = AddInvoice(_acme, "INV-2026-0002", 8_750.50m);

        var result = Evaluate(Incoming(5_000m, "INV-2026-0002 partial payment", AcmeIban));

        result.Allocations.Should().ContainSingle().Which.Should().BeEquivalentTo(new { InvoiceId = invoice.Id, Amount = 5_000m, Score = 95 });
        result.Allocations[0].Reason.Should().EndWith("partial payment");
    }

    [Fact]
    public void Overpayment_allocates_only_the_open_amount()
    {
        AddInvoice(_globex, "INV-2026-0004", 3_200m);

        var result = Evaluate(Incoming(3_500m, "INV-2026-0004", GlobexIban));

        result.Allocations.Should().ContainSingle().Which.Amount.Should().Be(3_200m);
        result.Allocations[0].Reason.Should().Contain("overpayment of 300.00 left unmatched");
        result.IsConfident(Threshold).Should().BeTrue();
    }

    [Fact]
    public void One_payment_for_several_invoices_is_split_by_due_date()
    {
        var later = AddInvoice(_acme, "INV-2026-0002", 500m, issued: new DateOnly(2026, 10, 15));
        var earlier = AddInvoice(_acme, "INV-2026-0001", 1_000m, issued: new DateOnly(2026, 10, 1));

        var result = Evaluate(Incoming(1_200m, "INV-2026-0002 and INV-2026-0001", AcmeIban));

        result.Allocations.Select(a => (a.InvoiceId, a.Amount)).Should().Equal((earlier.Id, 1_000m), (later.Id, 200m));
    }

    [Fact]
    public void Payment_covering_several_invoices_exactly_gets_the_exact_amount_points()
    {
        AddInvoice(_acme, "INV-2026-0001", 1_000m);
        AddInvoice(_acme, "INV-2026-0002", 500m);

        var result = Evaluate(Incoming(1_500m, "INV-2026-0001 INV-2026-0002", AcmeIban));

        result.Allocations.Should().HaveCount(2).And.OnlyContain(a => a.Score == 100);
    }

    [Fact]
    public void Reference_with_payer_of_another_customer_is_not_confident()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        var result = Evaluate(Incoming(100m, "INV-2026-0001", GlobexIban));

        result.HasProposal.Should().BeTrue();
        result.IsConfident(Threshold).Should().BeFalse();
        result.Allocations[0].Reason.Should().Contain("warning: payer looks like Globex Logistics Inc.");
    }

    [Fact]
    public void Reference_with_unknown_payer_and_exact_amount_is_confident()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        Evaluate(Incoming(100m, "INV-2026-0001")).Score.Should().Be(80);
    }

    [Fact]
    public void Reference_with_unknown_payer_and_partial_amount_needs_review()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        var result = Evaluate(Incoming(60m, "INV-2026-0001"));

        result.Score.Should().Be(75);
        result.IsConfident(Threshold).Should().BeFalse();
    }

    [Fact]
    public void Paid_or_cancelled_invoices_are_never_candidates()
    {
        var paid = AddInvoice(_acme, "INV-2026-0001", 100m);
        paid.ApplyPayment(100m);
        var cancelled = AddInvoice(_acme, "INV-2026-0002", 100m);
        cancelled.Cancel();

        var result = Evaluate(Incoming(100m, "INV-2026-0001 INV-2026-0002", AcmeIban));

        result.HasProposal.Should().BeFalse();
    }

    [Fact]
    public void Invoice_in_another_currency_is_not_a_candidate()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m, currency: "EUR");

        Evaluate(Incoming(100m, "INV-2026-0001", AcmeIban)).HasProposal.Should().BeFalse();
    }

    // ---- No reference: identify the payer ----

    [Fact]
    public void Known_iban_and_single_invoice_with_same_amount_is_confident()
    {
        var invoice = AddInvoice(_acme, "INV-2026-0001", 750m);
        AddInvoice(_acme, "INV-2026-0002", 900m);

        var result = Evaluate(Incoming(750m, "Transfer", AcmeIban));

        result.IsConfident(Threshold).Should().BeTrue();
        result.Allocations.Should().ContainSingle().Which.InvoiceId.Should().Be(invoice.Id);
    }

    [Fact]
    public void Tax_number_in_description_identifies_the_payer()
    {
        var invoice = AddInvoice(_hooli, "INV-2026-0007", 9_999.99m);
        AddInvoice(_hooli, "INV-2026-0008", 27_350m);

        var result = Evaluate(Incoming(9_999.99m, "VKN 4567890128 payment", iban: null));

        result.Allocations.Should().ContainSingle().Which.InvoiceId.Should().Be(invoice.Id);
        result.Score.Should().Be(80);
        result.Allocations[0].Reason.Should().StartWith("Payer identified by tax number in description");
    }

    [Fact]
    public void Ten_digit_number_that_is_not_a_valid_tax_number_is_ignored()
    {
        AddInvoice(_hooli, "INV-2026-0007", 100m);

        Evaluate(Incoming(100m, "Order 4567890129")).HasProposal.Should().BeFalse();
    }

    [Fact]
    public void Name_alone_is_not_enough_for_an_automatic_match()
    {
        AddInvoice(_hooli, "INV-2026-0007", 100m);

        var result = Evaluate(Incoming(100m, "Transfer", iban: null, name: "HOOLI FOODS CO"));

        result.Score.Should().Be(70);
        result.IsConfident(Threshold).Should().BeFalse();
    }

    [Fact]
    public void Two_invoices_with_the_same_amount_are_never_guessed()
    {
        AddInvoice(_acme, "INV-2026-0005", 15_000m);
        AddInvoice(_acme, "INV-2026-0006", 15_000m);

        var result = Evaluate(Incoming(15_000m, "Transfer", AcmeIban));

        result.HasProposal.Should().BeFalse();
        result.Explanation.Should().StartWith("Ambiguous: 2 open invoices of Acme Textiles Ltd.");
    }

    [Fact]
    public void Known_payer_without_invoice_of_that_amount_gets_no_proposal()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        var result = Evaluate(Incoming(99m, "Transfer", AcmeIban));

        result.HasProposal.Should().BeFalse();
        result.Explanation.Should().Contain("no open invoice has amount 99.00");
    }

    [Fact]
    public void Known_payer_without_open_invoices_gets_no_proposal()
    {
        AddInvoice(_globex, "INV-2026-0003", 100m);

        Evaluate(Incoming(100m, "Transfer", AcmeIban)).Explanation.Should().Contain("they have no open invoices");
    }

    [Fact]
    public void Unknown_payer_without_reference_gets_no_proposal()
    {
        AddInvoice(_acme, "INV-2026-0001", 1_234.56m);

        var result = Evaluate(Incoming(1_234.56m, "Transfer", iban: null, name: "JOHN DOE"));

        result.HasProposal.Should().BeFalse();
        result.Explanation.Should().Be("No invoice reference in the description and the payer is unknown.");
    }

    // ---- Transactions that are never matched ----

    [Fact]
    public void Outgoing_transaction_is_never_matched()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);

        Evaluate(Incoming(-100m, "INV-2026-0001", AcmeIban)).HasProposal.Should().BeFalse();
    }

    [Fact]
    public void Ignored_transaction_is_never_matched()
    {
        AddInvoice(_acme, "INV-2026-0001", 100m);
        var transaction = Incoming(100m, "INV-2026-0001", AcmeIban);
        transaction.Ignore();

        Evaluate(transaction).HasProposal.Should().BeFalse();
    }

    [Fact]
    public void Only_the_unmatched_part_of_a_transaction_is_allocated()
    {
        var first = AddInvoice(_acme, "INV-2026-0001", 300m);
        var second = AddInvoice(_acme, "INV-2026-0002", 200m);
        var transaction = Incoming(500m, "INV-2026-0001 INV-2026-0002", AcmeIban);
        Match.Create(transaction, first, 300m, 100, "manual", MatchMethod.Manual, "user", Now);

        var result = Evaluate(transaction);

        result.Allocations.Should().ContainSingle().Which.Should().BeEquivalentTo(new { InvoiceId = second.Id, Amount = 200m });
    }

    private MatchResult Evaluate(BankTransaction transaction) => _engine.Evaluate(transaction, _invoices, Customers);

    private Invoice AddInvoice(Customer customer, string number, decimal amount, DateOnly? issued = null, string currency = "TRY")
    {
        var issueDate = issued ?? new DateOnly(2026, 10, 1);
        var invoice = new Invoice(customer.Id, number, issueDate, issueDate.AddDays(30), amount, currency, Now);
        _invoices.Add(invoice);
        return invoice;
    }

    private static BankTransaction Incoming(decimal amount, string description, string? iban = null, string? name = null) =>
        new(Guid.NewGuid(), Guid.NewGuid().ToString(), new DateOnly(2026, 11, 1), amount, "TRY", description, name, iban, Now);

    private static Customer Customer(string name, string taxNumber, string? iban) =>
        new(name, TaxNumber.Create(taxNumber), iban is null ? null : Iban.Create(iban), null, Now);
}
