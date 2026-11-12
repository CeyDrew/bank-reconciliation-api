using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Application.Accounts;

public class AccountService(
    IApplicationDbContext db,
    IValidator<CreateAccountRequest> createValidator,
    IValidator<UpdateAccountRequest> updateValidator,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<AccountResponse>> ListAsync(CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        return accounts.Select(AccountResponse.From).ToList();
    }

    public async Task<AccountResponse> GetAsync(Guid id, CancellationToken ct) =>
        AccountResponse.From(await FindAsync(id, ct));

    public async Task<AccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var iban = Iban.Create(request.Iban);
        if (await db.Accounts.AnyAsync(a => a.Iban == iban, ct))
        {
            throw new ConflictException($"An account with IBAN {iban.Masked} already exists.");
        }

        var account = new Account(request.Name, iban, request.Currency, clock.GetUtcNow());
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return AccountResponse.From(account);
    }

    public async Task<AccountResponse> UpdateAsync(Guid id, UpdateAccountRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var account = await FindAsync(id, ct);

        account.Rename(request.Name);
        if (request.IsActive)
        {
            account.Activate();
        }
        else
        {
            account.Deactivate();
        }

        await db.SaveChangesAsync(ct);
        return AccountResponse.From(account);
    }

    private async Task<Account> FindAsync(Guid id, CancellationToken ct) =>
        await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Account", id);
}
