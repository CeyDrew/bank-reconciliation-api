using Reconciliation.Domain.Common;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Domain.Entities;

/// <summary>A person who can sign in to the API. Only a password hash is stored.</summary>
public class User : Entity
{
    public string Email { get; private init; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private User() { }

    public User(string email, UserRole role, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Email is required.");
        }

        Email = email.Trim().ToLowerInvariant();
        Role = role;
        IsActive = true;
        CreatedAt = createdAt;
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash is required.");
        }

        PasswordHash = passwordHash;
    }

    public void Update(UserRole role, bool isActive)
    {
        Role = role;
        IsActive = isActive;
    }
}
