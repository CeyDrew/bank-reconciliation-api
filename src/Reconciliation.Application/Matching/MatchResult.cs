namespace Reconciliation.Application.Matching;

/// <summary>Part of a transaction proposed for one invoice.</summary>
public record MatchAllocation(Guid InvoiceId, string InvoiceNumber, decimal Amount, int Score, string Reason);

/// <summary>What the engine thinks a transaction pays. No allocations means no proposal.</summary>
public record MatchResult(IReadOnlyList<MatchAllocation> Allocations, string Explanation)
{
    public bool HasProposal => Allocations.Count > 0;

    /// <summary>The weakest allocation decides how confident the whole proposal is.</summary>
    public int Score => HasProposal ? Allocations.Min(a => a.Score) : 0;

    public bool IsConfident(int threshold) => HasProposal && Score >= threshold;

    public static MatchResult None(string explanation) => new([], explanation);
}

/// <summary>Points per signal. A proposal at or above the auto-match threshold (default 80) is applied without review.</summary>
public static class MatchScores
{
    public const int InvoiceNumberInDescription = 70;
    public const int InvoiceDigitsInDescription = 60;

    public const int PayerByIban = 20;
    public const int PayerByTaxNumber = 20;
    public const int PayerByName = 10;

    /// <summary>The invoice belongs to a different customer than the identified payer.</summary>
    public const int PayerMismatch = -40;

    public const int AmountEqualsOpenAmount = 10;
    public const int AmountBelowOpenAmount = 5;

    /// <summary>No reference, but exactly one open invoice of the payer has this amount.</summary>
    public const int SingleInvoiceWithSameAmount = 50;
}
