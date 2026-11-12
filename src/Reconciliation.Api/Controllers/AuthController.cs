using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Reconciliation.Api.Auth;
using Reconciliation.Application.Auth;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Exchanges email and password for a bearer token (valid for 60 minutes).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.LoginRateLimit)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var token = await auth.LoginAsync(request, ct);
        return token is null
            ? Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login failed", detail: "Email or password is wrong.")
            : Ok(token);
    }
}
