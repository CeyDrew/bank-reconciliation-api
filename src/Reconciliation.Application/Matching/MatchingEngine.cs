using System.Globalization;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Application.Matching;

/// <summary>
/// Decides which open invoices an incoming transaction pays, with a score and a readable reason.
/// Pure logic: it changes nothing and doesn't touch the database, so every rule is unit-testable.
///
/// 1. If the description names open invoices, the payment goes to them (oldest due date first).
/// 2. Otherwise, if the payer is known and exactly one of their open invoices has this amount, it goes there.
/// 3. Otherwise there is no proposal. The engine never guesses between equal candidates.
/// </summary>
public class MatchingEngine
{
    public MatchResult Evaluate(BankTransaction transaction, IEnumerable<Invoice> openInvoices, IReadOnlyCollection<Customer> customers)
    {
        if (!transaction.IsIncoming)
        {
            return MatchResult.None("Outgoing transaction; only incoming payments are matched.");
        }

        if (transaction.Status == TransactionStatus.Ignored)
        {
            return MatchResult.None("Transaction is ignored.");
        }

        var amount = transaction.UnmatchedAmount;
        if (amount <= 0)
        {
            return MatchResult.None("Transaction is already fully matched.");
        }

        var candidates = openInvoices
            .Where(i => i.Status is InvoiceStatus.Open or InvoiceStatus.PartiallyPaid
                        && i.OpenAmount > 0
                        && i.Currency == transaction.Currency)
            .ToList();
        var payer = IdentifyPayer(transaction, customers);
        var references = FindReferences(transaction.Description, candidates);

        return references.Count > 0
            ? MatchByReference(amount, references, payer)
            : MatchByPayer(amount, candidates, payer);
    }

    private static MatchResult MatchByReference(decimal amount, List<Reference> references, Payer? payer)
    {
        var ordered = references.OrderBy(r => r.Invoice.DueDate).ThenBy(r => r.Invoice.Number, StringComparer.Ordinal).ToList();
        var totalOpen = ordered.Sum(r => r.Invoice.OpenAmount);
        var (amountPoints, amountText) =
            amount == totalOpen ? (MatchScores.AmountEqualsOpenAmount, "amount equals open amount")
            : amount < totalOpen ? (MatchScores.AmountBelowOpenAmount, "partial payment")
            : (0, $"overpayment of {Format(amount - totalOpen)} left unmatched");

        var allocations = new List<MatchAllocation>();
        var remaining = amount;
        foreach (var reference in ordered)
        {
            var allocated = Math.Min(remaining, reference.Invoice.OpenAmount);
            if (allocated <= 0)
            {
                break;
            }

            var (payerPoints, payerText) = PayerPoints(payer, reference.Invoice);
            var score = reference.Points + payerPoints + amountPoints;
            var reason = string.Join("; ", new[] { reference.Text, payerText, amountText }.Where(t => t is not null));

            allocations.Add(new MatchAllocation(reference.Invoice.Id, reference.Invoice.Number, allocated, Math.Clamp(score, 0, 100), reason));
            remaining -= allocated;
        }

        return new MatchResult(allocations, $"Description references {ordered.Count} open invoice(s).");
    }

    private static MatchResult MatchByPayer(decimal amount, List<Invoice> candidates, Payer? payer)
    {
        if (payer is null)
        {
            return MatchResult.None("No invoice reference in the description and the payer is unknown.");
        }

        var name = payer.Customer.Name;
        var payerInvoices = candidates.Where(i => i.CustomerId == payer.Customer.Id).ToList();
        if (payerInvoices.Count == 0)
        {
            return MatchResult.None($"Payer identified as {name} by {payer.Evidence}, but they have no open invoices.");
        }

        var sameAmount = payerInvoices.Where(i => i.OpenAmount == amount).ToList();
        switch (sameAmount.Count)
        {
            case 1:
                var invoice = sameAmount[0];
                var score = payer.Points + MatchScores.AmountEqualsOpenAmount + MatchScores.SingleInvoiceWithSameAmount;
                var reason = $"Payer identified by {payer.Evidence}; only open invoice of {name} with amount {Format(amount)}";
                return new MatchResult([new MatchAllocation(invoice.Id, invoice.Number, amount, score, reason)], reason + ".");
            case > 1:
                return MatchResult.None(
                    $"Ambiguous: {sameAmount.Count} open invoices of {name} have amount {Format(amount)}. Needs manual matching.");
            default:
                return MatchResult.None($"Payer identified as {name}, but no open invoice has amount {Format(amount)}.");
        }
    }

    private static (int Points, string? Text) PayerPoints(Payer? payer, Invoice invoice) =>
        payer is null ? (0, null)
        : payer.Customer.Id == invoice.CustomerId ? (payer.Points, $"payer identified by {payer.Evidence}")
        : (MatchScores.PayerMismatch, $"warning: payer looks like {payer.Customer.Name} ({payer.Evidence}), not the invoice's customer");

    private static List<Reference> FindReferences(string description, List<Invoice> candidates)
    {
        var text = TextMatching.Normalize(description);
        var references = new List<Reference>();

        foreach (var invoice in candidates)
        {
            var number = TextMatching.Normalize(invoice.Number);
            var digits = TextMatching.DigitsOnly(number);

            if (TextMatching.ContainsToken(text, number))
            {
                references.Add(new Reference(invoice, MatchScores.InvoiceNumberInDescription,
                    $"invoice number {invoice.Number} in description"));
            }
            else if (digits.Length >= 6 && TextMatching.ContainsToken(text, digits))
            {
                // "Invoice 2026 0003" still identifies INV-2026-0003. Short digit runs are too likely to be coincidence.
                references.Add(new Reference(invoice, MatchScores.InvoiceDigitsInDescription,
                    $"digits of invoice number {invoice.Number} in description"));
            }
        }

        return references;
    }

    /// <summary>Strongest evidence first: IBAN, then tax number in the description, then the counterparty name.</summary>
    private static Payer? IdentifyPayer(BankTransaction transaction, IReadOnlyCollection<Customer> customers)
    {
        if (Iban.TryCreate(transaction.CounterpartyIban, out var iban)
            && customers.FirstOrDefault(c => c.Iban == iban) is { } byIban)
        {
            return new Payer(byIban, MatchScores.PayerByIban, "counterparty IBAN");
        }

        foreach (var candidate in TextMatching.TenDigitNumbers(transaction.Description))
        {
            if (TaxNumber.TryCreate(candidate, out var taxNumber)
                && customers.FirstOrDefault(c => c.TaxNumber == taxNumber) is { } byTaxNumber)
            {
                return new Payer(byTaxNumber, MatchScores.PayerByTaxNumber, "tax number in description");
            }
        }

        var byName = customers.Where(c => TextMatching.SameCompanyName(c.Name, transaction.CounterpartyName)).ToList();
        return byName.Count == 1 ? new Payer(byName[0], MatchScores.PayerByName, "counterparty name") : null;
    }

    private static string Format(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private record Reference(Invoice Invoice, int Points, string Text);

    private record Payer(Customer Customer, int Points, string Evidence);
}
