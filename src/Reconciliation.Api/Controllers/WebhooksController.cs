using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Auth;
using Reconciliation.Application.Common;
using Reconciliation.Application.Webhooks;

namespace Reconciliation.Api.Controllers;

/// <summary>Outbox of events sent to the ERP. Admins only.</summary>
[ApiController]
[Route("api/v1/webhooks")]
[Produces("application/json")]
[Authorize(Roles = Roles.Admin)]
public class WebhooksController(WebhookAdminService webhooks, WebhookDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists deliveries, newest first. Filter by status=Failed to see what needs attention.</summary>
    [HttpGet("deliveries")]
    public Task<PagedResult<WebhookDeliveryResponse>> List([FromQuery] WebhookDeliveryQuery query, CancellationToken ct) =>
        webhooks.ListAsync(query, ct);

    /// <summary>Queues a failed delivery again, e.g. after the ERP has been fixed.</summary>
    [HttpPost("deliveries/{id:guid}/retry")]
    [ProducesResponseType<WebhookDeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<WebhookDeliveryResponse> Retry(Guid id, CancellationToken ct) => webhooks.RetryAsync(id, ct);

    /// <summary>Sends due deliveries now instead of waiting for the dispatch job.</summary>
    [HttpPost("dispatch")]
    [ProducesResponseType<WebhookDispatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WebhookDispatchResult>> Dispatch(CancellationToken ct)
    {
        var result = await dispatcher.DispatchAsync(ct);
        return result is null
            ? Problem(statusCode: StatusCodes.Status409Conflict, title: "Dispatch already running")
            : Ok(result);
    }
}
