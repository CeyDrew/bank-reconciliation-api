using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Common;
using Reconciliation.Application.Invoices;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1/invoices")]
[Produces("application/json")]
public class InvoicesController(InvoiceService invoices) : ControllerBase
{
    /// <summary>Lists invoices ordered by due date, optionally filtered by customer and status.</summary>
    [HttpGet]
    public Task<PagedResult<InvoiceResponse>> List([FromQuery] InvoiceQuery query, CancellationToken ct) =>
        invoices.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<InvoiceResponse> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InvoiceResponse>> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await invoices.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
    }

    /// <summary>Changes an invoice that has no payments yet.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<InvoiceResponse> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct) =>
        invoices.UpdateAsync(id, request, ct);

    /// <summary>Cancels an invoice that has no payments. Invoices are never deleted.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<InvoiceResponse> Cancel(Guid id, CancellationToken ct) => invoices.CancelAsync(id, ct);
}
