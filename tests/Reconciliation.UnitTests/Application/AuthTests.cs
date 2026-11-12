using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Auth;
using Reconciliation.Application.Common;
using Reconciliation.Application.Users;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;
using Reconciliation.Infrastructure.Auth;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests.Application;

public class AuthTests
{
    private const string Password = "correct horse battery staple";
    private static readonly JwtOptions Jwt = new() { SigningKey = new string('k', 48) };

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db = TestDb.Create();
    private readonly IdentityPasswordHasher _hasher = new();
    private readonly User _accountant;

    public AuthTests()
    {
        _accountant = new User("Accountant@Example.com", UserRole.Accountant, _clock.GetUtcNow());
        _accountant.SetPasswordHash(_hasher.Hash(Password));
        _db.Users.Add(_accountant);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Correct_password_returns_a_token_that_validates()
    {
        var token = await CreateAuthService().LoginAsync(new LoginRequest(" accountant@example.com ", Password), CancellationToken.None);

        token.Should().NotBeNull();
        token!.Role.Should().Be("Accountant");
        token.ExpiresAt.Should().Be(_clock.GetUtcNow().AddMinutes(60));

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = Jwt.Issuer,
            ValidAudience = Jwt.Audience,
            IssuerSigningKey = Jwt.CreateKey(),
            LifetimeValidator = (_, _, _, _) => true // FakeTimeProvider time is not the real clock
        });
        validation.IsValid.Should().BeTrue();
        validation.Claims.Should().Contain(new KeyValuePair<string, object>("role", "Accountant"))
            .And.Contain(new KeyValuePair<string, object>("sub", _accountant.Id.ToString()));
        _db.AuditEntries.Should().ContainSingle(a => a.Action == "auth.login_succeeded");
    }

    [Theory]
    [InlineData("accountant@example.com", "wrong password!")]
    [InlineData("nobody@example.com", Password)]
    public async Task Wrong_password_or_unknown_email_returns_null_and_is_audited(string email, string password)
    {
        var token = await CreateAuthService().LoginAsync(new LoginRequest(email, password), CancellationToken.None);

        token.Should().BeNull();
        _db.AuditEntries.Should().ContainSingle(a => a.Action == "auth.login_failed");
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        _accountant.Update(UserRole.Accountant, isActive: false);
        await _db.SaveChangesAsync();

        (await CreateAuthService().LoginAsync(new LoginRequest("accountant@example.com", Password), CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public void Hasher_never_accepts_a_missing_hash()
    {
        _hasher.Verify(null, Password).Should().BeFalse();
        _hasher.Verify(_hasher.Hash(Password), Password).Should().BeTrue();
    }

    [Fact]
    public async Task Admin_cannot_remove_own_admin_access()
    {
        var admin = new User("admin@example.com", UserRole.Admin, _clock.GetUtcNow());
        admin.SetPasswordHash(_hasher.Hash(Password));
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();
        var service = CreateUserService(new StubCurrentUser(admin.Email, isAdmin: true, userId: admin.Id));

        var act = () => service.UpdateAsync(admin.Id, new UpdateUserRequest(UserRole.Accountant, true), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Created_user_has_a_hashed_password_and_no_password_in_the_audit_log()
    {
        var admin = new StubCurrentUser("admin@example.com", isAdmin: true);

        var created = await CreateUserService(admin).CreateAsync(
            new CreateUserRequest("new@example.com", "a long enough password", UserRole.Accountant), CancellationToken.None);

        var stored = _db.Users.Single(u => u.Id == created.Id);
        stored.PasswordHash.Should().NotContain("a long enough password");
        _hasher.Verify(stored.PasswordHash, "a long enough password").Should().BeTrue();
        _db.AuditEntries.Single(a => a.Action == "user.created").Details.Should().NotContain("password");
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var act = () => CreateUserService(new StubCurrentUser(isAdmin: true)).CreateAsync(
            new CreateUserRequest("new@example.com", "short", UserRole.Accountant), CancellationToken.None);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    private AuthService CreateAuthService() =>
        new(_db, _hasher, new JwtTokenService(Options.Create(Jwt), _clock),
            new AuditTrail(_db, new StubCurrentUser("anonymous"), _clock), new LoginRequestValidator());

    private UserService CreateUserService(StubCurrentUser user) =>
        new(_db, _hasher, user, new AuditTrail(_db, user, _clock), new CreateUserRequestValidator(),
            new UpdateUserRequestValidator(), _clock);
}
