using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Common;
using Reconciliation.Application.Matching;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ReconciliationController(ReconciliationService reconciliation, MatchQueries matches) : ControllerBase
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

    /// <summary>Lists matches, newest first, filtered by transaction, invoice or method.</summary>
    [HttpGet("matches")]
    public Task<PagedResult<MatchResponse>> ListMatches([FromQuery] MatchQuery query, CancellationToken ct) =>
        matches.ListAsync(query, ct);
}
