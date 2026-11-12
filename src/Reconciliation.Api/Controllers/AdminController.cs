using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Auth;
using Reconciliation.Application.Common;
using Reconciliation.Application.Users;

namespace Reconciliation.Api.Controllers;

/// <summary>User administration and the audit log. Admins only.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize(Roles = Roles.Admin)]
public class AdminController(UserService users, AuditQueries auditLog) : ControllerBase
{
    [HttpGet("users")]
    public Task<IReadOnlyList<UserResponse>> ListUsers(CancellationToken ct) => users.ListAsync(ct);

    /// <summary>Creates a user. Passwords need at least 12 characters.</summary>
    [HttpPost("users")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, user);
    }

    /// <summary>Changes a user's role or deactivates them. Admins can't remove their own admin access.</summary>
    [HttpPut("users/{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<UserResponse> UpdateUser(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, ct);

    /// <summary>Who did what and when, newest first.</summary>
    [HttpGet("audit-log")]
    public Task<PagedResult<AuditEntryResponse>> AuditLog([FromQuery] AuditQuery query, CancellationToken ct) =>
        auditLog.ListAsync(query, ct);
}
