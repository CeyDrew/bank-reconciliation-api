using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auth;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Infrastructure.Auth;

/// <summary>Bound from "Jwt". The signing key comes from the environment (.env / user secrets), never from source.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "bank-reconciliation-api";

    [Required]
    public string Audience { get; init; } = "bank-reconciliation-api";

    /// <summary>HMAC-SHA256 key; at least 32 bytes so it is as long as the hash output.</summary>
    [Required, MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters. Set JWT_SIGNING_KEY in .env.")]
    public string SigningKey { get; init; } = "";

    [Range(1, 24 * 60)]
    public int LifetimeMinutes { get; init; } = 60;

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class AppClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
}

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public TokenResponse CreateToken(User user)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.LifetimeMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(AppClaims.Subject, user.Id.ToString()),
                new Claim(AppClaims.Name, user.Email),
                new Claim(AppClaims.Role, user.Role.ToString())
            ]),
            SigningCredentials = new SigningCredentials(settings.CreateKey(), SecurityAlgorithms.HmacSha256)
        });

        return new TokenResponse(token, expiresAt, user.Role.ToString());
    }
}
