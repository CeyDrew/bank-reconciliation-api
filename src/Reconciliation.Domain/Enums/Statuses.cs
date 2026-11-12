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

public enum UserRole
{
    Admin,
    Accountant
}

public enum WebhookStatus
{
    Pending,
    Delivered,

    /// <summary>Gave up: rejected by the ERP or out of attempts. Needs a person.</summary>
    Failed
}
