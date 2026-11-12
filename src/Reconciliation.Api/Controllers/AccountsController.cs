using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Accounts;

namespace Reconciliation.Api.Controllers;

/// <summary>Our own bank accounts. Accounts are deactivated rather than deleted.</summary>
[ApiController]
[Route("api/v1/accounts")]
[Produces("application/json")]
public class AccountsController(AccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AccountResponse>> List(CancellationToken ct) => accounts.ListAsync(ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountResponse> Get(Guid id, CancellationToken ct) => accounts.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountResponse>> Create(CreateAccountRequest request, CancellationToken ct)
    {
        var account = await accounts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
    }

    /// <summary>Renames or (de)activates an account.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountResponse> Update(Guid id, UpdateAccountRequest request, CancellationToken ct) =>
        accounts.UpdateAsync(id, request, ct);
}
