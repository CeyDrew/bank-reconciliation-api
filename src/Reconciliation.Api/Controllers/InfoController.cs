using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class InfoController(IWebHostEnvironment environment) : ControllerBase
{
    public record InfoResponse(string Name, string Version, string Environment);

    /// <summary>Returns service name, version and environment.</summary>
    [HttpGet]
    [ProducesResponseType<InfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<InfoResponse> Get()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        return Ok(new InfoResponse("bank-reconciliation-api", version, environment.EnvironmentName));
    }
}
