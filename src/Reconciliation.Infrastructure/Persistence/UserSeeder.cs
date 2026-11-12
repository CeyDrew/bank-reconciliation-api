using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Reconciliation.Application.Abstractions;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Infrastructure.Persistence;

/// <summary>Initial passwords for the demo users. They come from the environment, never from source.</summary>
public class SeedUserOptions
{
    public const string SectionName = "SeedUsers";

    public string? AdminPassword { get; init; }
    public string? AccountantPassword { get; init; }
}

/// <summary>Creates admin@example.com and accountant@example.com when the user table is empty.</summary>
internal static class UserSeeder
{
    public const string AdminEmail = "admin@example.com";
    public const string AccountantEmail = "accountant@example.com";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher hasher, SeedUserOptions options, TimeProvider clock,
        ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.AdminPassword) || string.IsNullOrWhiteSpace(options.AccountantPassword))
        {
            logger.LogWarning("No users seeded: set SeedUsers__AdminPassword and SeedUsers__AccountantPassword");
            return;
        }

        db.Users.AddRange(
            Create(AdminEmail, UserRole.Admin, options.AdminPassword),
            Create(AccountantEmail, UserRole.Accountant, options.AccountantPassword));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded users {Admin} and {Accountant}", AdminEmail, AccountantEmail);

        User Create(string email, UserRole role, string password)
        {
            var user = new User(email, role, clock.GetUtcNow());
            user.SetPasswordHash(hasher.Hash(password));
            return user;
        }
    }
}
