using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Common;
using Reconciliation.Application.Transactions;

namespace Reconciliation.Api.Controllers;

/// <summary>Read-only: transactions are imported from the bank by a background job.</summary>
[ApiController]
[Route("api/v1/transactions")]
[Produces("application/json")]
public class TransactionsController(TransactionService transactions) : ControllerBase
{
    /// <summary>Lists transactions, newest first, filtered by account, status and booking date range.</summary>
    [HttpGet]
    public Task<PagedResult<TransactionResponse>> List([FromQuery] TransactionQuery query, CancellationToken ct) =>
        transactions.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TransactionResponse> Get(Guid id, CancellationToken ct) => transactions.GetAsync(id, ct);
}
