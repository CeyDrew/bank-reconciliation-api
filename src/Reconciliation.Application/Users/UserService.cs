using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Users;

public record UserResponse(Guid Id, string Email, UserRole Role, bool IsActive, DateTimeOffset CreatedAt)
{
    public static UserResponse From(User user) => new(user.Id, user.Email, user.Role, user.IsActive, user.CreatedAt);
}

public record CreateUserRequest(string Email, string Password, UserRole Role);

public record UpdateUserRequest(UserRole Role, bool IsActive);

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public const int MinPasswordLength = 12;

    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        // Length beats complexity rules; the hasher handles any characters.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(MinPasswordLength).MaximumLength(200);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
    }
}

/// <summary>User administration. Only admins reach these endpoints.</summary>
public class UserService(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ICurrentUser currentUser,
    AuditTrail audit,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<UserResponse>> ListAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        return users.Select(UserResponse.From).ToList();
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflictException($"A user with email {email} already exists.");
        }

        var user = new User(email, request.Role, clock.GetUtcNow());
        user.SetPasswordHash(hasher.Hash(request.Password));
        db.Users.Add(user);
        audit.Record("user.created", "User", user.Id, new { user.Email, Role = user.Role.ToString() });
        await db.SaveChangesAsync(ct);

        return UserResponse.From(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);

        if (user.Id == currentUser.UserId && (request.Role != UserRole.Admin || !request.IsActive))
        {
            throw new ConflictException("You can't remove your own admin access.");
        }

        user.Update(request.Role, request.IsActive);
        audit.Record("user.updated", "User", user.Id, new { Role = user.Role.ToString(), user.IsActive });
        await db.SaveChangesAsync(ct);

        return UserResponse.From(user);
    }
}
