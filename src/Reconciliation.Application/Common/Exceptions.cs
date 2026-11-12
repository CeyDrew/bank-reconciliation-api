namespace Reconciliation.Application.Common;

/// <summary>The requested resource doesn't exist. Mapped to HTTP 404.</summary>
public class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.");

/// <summary>The request conflicts with the current state, e.g. a duplicate key. Mapped to HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);
