using Microsoft.AspNetCore.Identity;
using Reconciliation.Domain.Entities;
using IPasswordHasher = Reconciliation.Application.Abstractions.IPasswordHasher;

namespace Reconciliation.Infrastructure.Auth;

/// <summary>ASP.NET Core Identity's hasher: PBKDF2 with HMAC-SHA512, random salt, 100,000 iterations.</summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly Lazy<string> _dummyHash;

    public IdentityPasswordHasher()
    {
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(null!, Guid.NewGuid().ToString()));
    }

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string? hash, string password)
    {
        // Same amount of work whether or not the user exists.
        var result = _hasher.VerifyHashedPassword(null!, hash ?? _dummyHash.Value, password);
        return hash is not null && result != PasswordVerificationResult.Failed;
    }
}
