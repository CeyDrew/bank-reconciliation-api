using System.Security.Claims;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auth;
using Reconciliation.Application.Matching;
using Reconciliation.Infrastructure.Auth;

namespace Reconciliation.Api.Auth;

/// <summary>Reads the caller from the validated JWT. Without a request (scheduled jobs) the caller is the system.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } principal ? principal : null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(AppClaims.Subject), out var id) ? id : null;

    public string UserName => Principal?.Identity?.Name ?? ReconciliationService.SystemUser;

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) ?? false;
}
