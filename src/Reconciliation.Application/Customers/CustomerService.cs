using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Application.Customers;

public class CustomerService(
    IApplicationDbContext db,
    IValidator<SaveCustomerRequest> validator,
    ICurrentUser currentUser,
    AuditTrail audit,
    TimeProvider clock)
{
    public Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct)
    {
        var customers = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            customers = TaxNumber.TryCreate(search, out var taxNumber)
                ? customers.Where(c => c.TaxNumber == taxNumber)
                : customers.Where(c => c.Name.Contains(search));
        }

        return customers.OrderBy(c => c.Name).ToPagedResultAsync(query, ToResponse, ct);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct) => ToResponse(await FindAsync(id, ct));

    public async Task<CustomerResponse> CreateAsync(SaveCustomerRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var taxNumber = TaxNumber.Create(request.TaxNumber);
        await EnsureTaxNumberIsFreeAsync(taxNumber, exceptId: null, ct);

        var customer = new Customer(request.Name, taxNumber, ParseIban(request.Iban), request.Email, clock.GetUtcNow());
        db.Customers.Add(customer);
        audit.Record("customer.created", "Customer", customer.Id, AuditDetails(customer));
        await db.SaveChangesAsync(ct);

        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var customer = await FindAsync(id, ct);
        var taxNumber = TaxNumber.Create(request.TaxNumber);
        await EnsureTaxNumberIsFreeAsync(taxNumber, exceptId: id, ct);

        customer.Update(request.Name, taxNumber, ParseIban(request.Iban), request.Email);
        audit.Record("customer.updated", "Customer", customer.Id, AuditDetails(customer));
        await db.SaveChangesAsync(ct);

        return ToResponse(customer);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        if (await db.Invoices.AnyAsync(i => i.CustomerId == id, ct))
        {
            throw new ConflictException("Customer has invoices and can't be deleted.");
        }

        db.Customers.Remove(customer);
        audit.Record("customer.deleted", "Customer", customer.Id, AuditDetails(customer));
        await db.SaveChangesAsync(ct);
    }

    private CustomerResponse ToResponse(Customer customer) => CustomerResponse.From(customer, currentUser.IsAdmin);

    private static object AuditDetails(Customer c) =>
        new { c.Name, TaxNumber = c.TaxNumber.Value, Iban = c.Iban?.Masked, c.Email };

    private async Task<Customer> FindAsync(Guid id, CancellationToken ct) =>
        await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);

    private async Task EnsureTaxNumberIsFreeAsync(TaxNumber taxNumber, Guid? exceptId, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.TaxNumber == taxNumber && c.Id != exceptId, ct))
        {
            throw new ConflictException($"A customer with tax number {taxNumber} already exists.");
        }
    }

    private static Iban? ParseIban(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Iban.Create(value);
}
