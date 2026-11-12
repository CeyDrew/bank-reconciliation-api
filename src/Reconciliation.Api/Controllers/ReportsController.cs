using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Reports;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Produces("application/json")]
public class ReportsController(ReconciliationReportService reports) : ControllerBase
{
    /// <summary>
    /// Reconciliation summary per booking day: money received, matched, still unmatched (the difference to explain),
    /// ignored and paid out, plus totals and the open invoices. Defaults to today; at most 92 days.
    /// </summary>
    [HttpGet("reconciliation")]
    [ProducesResponseType<ReconciliationReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<ReconciliationReport> Reconciliation([FromQuery] ReconciliationReportQuery query, CancellationToken ct) =>
        reports.GetAsync(query, ct);
}
