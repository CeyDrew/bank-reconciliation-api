namespace Reconciliation.Domain.Common;

/// <summary>Thrown when an operation would break a business rule.</summary>
public class DomainException(string message) : Exception(message);
