using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Common;
using Reconciliation.Application.Matching;
using Reconciliation.Application.Transactions;

namespace Reconciliation.Api.Controllers;

/// <summary>Automatic and manual matching. Available to admins and accountants.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ReconciliationController(
    ReconciliationService reconciliation,
    ManualMatchingService manual,
    MatchQueries matches) : ControllerBase
{
    /// <summary>Matches unmatched incoming transactions to open invoices now, without waiting for the scheduled job.</summary>
    [HttpPost("reconciliation/run")]
    [ProducesResponseType<ReconciliationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReconciliationResult>> Run(CancellationToken ct)
    {
        var result = await reconciliation.RunAsync(ct);
        return result is null
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Reconciliation already running",
                detail: "Another reconciliation run is in progress. Try again when it has finished.")
            : Ok(result);
    }

    /// <summary>Shows what the matching engine proposes for a transaction, with score and reasons. Changes nothing.</summary>
    [HttpGet("transactions/{id:guid}/suggestions")]
    [ProducesResponseType<SuggestionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SuggestionResponse> Suggest(Guid id, CancellationToken ct) => reconciliation.SuggestAsync(id, ct);

    /// <summary>Matches a transaction to one or more invoices by hand. Amounts may not exceed the open amounts.</summary>
    [HttpPost("transactions/{id:guid}/matches")]
    [ProducesResponseType<IReadOnlyList<MatchResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<MatchResponse>>> Match(Guid id, ManualMatchRequest request, CancellationToken ct)
    {
        var created = await manual.MatchAsync(id, request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Marks a transaction as needing no reconciliation, e.g. a bank fee.</summary>
    [HttpPost("transactions/{id:guid}/ignore")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<TransactionResponse> Ignore(Guid id, ReasonRequest request, CancellationToken ct) =>
        manual.IgnoreAsync(id, request, ct);

    /// <summary>Puts an ignored transaction back into reconciliation.</summary>
    [HttpPost("transactions/{id:guid}/restore")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<TransactionResponse> Restore(Guid id, CancellationToken ct) => manual.RestoreAsync(id, ct);

    /// <summary>Lists matches, newest first, filtered by transaction, invoice or method.</summary>
    [HttpGet("matches")]
    public Task<PagedResult<MatchResponse>> ListMatches([FromQuery] MatchQuery query, CancellationToken ct) =>
        matches.ListAsync(query, ct);

    /// <summary>
    /// Undoes a match: the invoice is open again and the transaction unmatched. The transaction is then
    /// excluded from automatic matching, so the engine doesn't re-apply what a person rejected.
    /// </summary>
    [HttpDelete("matches/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonRequest request, CancellationToken ct)
    {
        await manual.ReverseAsync(id, request, ct);
        return NoContent();
    }
}
