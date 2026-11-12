using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auditing;

namespace Reconciliation.Application.Auth;

public static class Roles
{
    public const string Admin = nameof(Domain.Enums.UserRole.Admin);
    public const string Accountant = nameof(Domain.Enums.UserRole.Accountant);
}

public record LoginRequest(string Email, string Password);

public record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string Role);

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public class AuthService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    AuditTrail audit,
    IValidator<LoginRequest> validator)
{
    /// <summary>Returns a token, or null for any failure. The caller can't tell a wrong password from an unknown email.</summary>
    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        var passwordOk = hasher.Verify(user?.PasswordHash, request.Password);
        if (user is null || !passwordOk || !user.IsActive)
        {
            var reason = user is null ? "unknown email" : !passwordOk ? "wrong password" : "user inactive";
            audit.RecordAs(user?.Id, email, "auth.login_failed", "User", user?.Id, new { reason });
            await db.SaveChangesAsync(ct);
            return null;
        }

        audit.RecordAs(user.Id, user.Email, "auth.login_succeeded", "User", user.Id);
        await db.SaveChangesAsync(ct);
        return tokens.CreateToken(user);
    }
}
