using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Invoices;

public class InvoiceService(
    IApplicationDbContext db,
    IValidator<CreateInvoiceRequest> createValidator,
    IValidator<UpdateInvoiceRequest> updateValidator,
    TimeProvider clock)
{
    public Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        var invoices = db.Invoices.AsNoTracking().Include(i => i.Customer).AsQueryable();

        if (query.CustomerId is { } customerId)
        {
            invoices = invoices.Where(i => i.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            invoices = invoices.Where(i => i.Status == status);
        }

        return invoices
            .OrderBy(i => i.DueDate).ThenBy(i => i.Number)
            .ToPagedResultAsync(query, InvoiceResponse.From, ct);
    }

    public async Task<InvoiceResponse> GetAsync(Guid id, CancellationToken ct) =>
        InvoiceResponse.From(await FindAsync(id, ct));

    public async Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct))
        {
            throw new NotFoundException("Customer", request.CustomerId);
        }

        await EnsureNumberIsFreeAsync(request.Number, exceptId: null, ct);

        var invoice = new Invoice(request.CustomerId, request.Number, request.IssueDate, request.DueDate,
            request.Amount, request.Currency, clock.GetUtcNow());
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        return await GetAsync(invoice.Id, ct);
    }

    public async Task<InvoiceResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var invoice = await FindAsync(id, ct);
        await EnsureNumberIsFreeAsync(request.Number, exceptId: id, ct);

        invoice.Update(request.Number, request.IssueDate, request.DueDate, request.Amount);
        await db.SaveChangesAsync(ct);

        return InvoiceResponse.From(invoice);
    }

    public async Task<InvoiceResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);
        invoice.Cancel();
        await db.SaveChangesAsync(ct);

        return InvoiceResponse.From(invoice);
    }

    private async Task<Invoice> FindAsync(Guid id, CancellationToken ct) =>
        await db.Invoices.Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException("Invoice", id);

    private async Task EnsureNumberIsFreeAsync(string number, Guid? exceptId, CancellationToken ct)
    {
        var trimmed = number.Trim();
        if (await db.Invoices.AnyAsync(i => i.Number == trimmed && i.Id != exceptId, ct))
        {
            throw new ConflictException($"Invoice number {trimmed} is already used.");
        }
    }
}
