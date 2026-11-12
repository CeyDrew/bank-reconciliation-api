namespace Reconciliation.Domain.Enums;

public enum InvoiceStatus
{
    Open,
    PartiallyPaid,
    Paid,
    Cancelled
}

public enum TransactionStatus
{
    Unmatched,
    PartiallyMatched,
    Matched,
    Ignored
}

public enum MatchMethod
{
    Automatic,
    Manual
}
