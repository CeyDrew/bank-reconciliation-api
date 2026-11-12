using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reconciliation.Application.Auth;
using Reconciliation.Application.Common;
using Reconciliation.Application.Customers;

namespace Reconciliation.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Produces("application/json")]
public class CustomersController(CustomerService customers) : ControllerBase
{
    /// <summary>Lists customers, optionally filtered by name or exact tax number.</summary>
    [HttpGet]
    public Task<PagedResult<CustomerResponse>> List([FromQuery] CustomerQuery query, CancellationToken ct) =>
        customers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CustomerResponse> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<CustomerResponse> Update(Guid id, SaveCustomerRequest request, CancellationToken ct) =>
        customers.UpdateAsync(id, request, ct);

    /// <summary>Deletes a customer that has no invoices. Admins only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }
}
