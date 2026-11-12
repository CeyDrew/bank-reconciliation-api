using Reconciliation.Application.Auth;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Abstractions;

/// <summary>Who is making the current request. Outside a request (scheduled jobs) this is the system.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string UserName { get; }
    bool IsAdmin { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks a password. A null hash (unknown user) is still checked against a dummy hash, so the response time
    /// doesn't tell an attacker whether an email exists; the result is then always false.
    /// </summary>
    bool Verify(string? hash, string password);
}

public interface ITokenService
{
    TokenResponse CreateToken(User user);
}
