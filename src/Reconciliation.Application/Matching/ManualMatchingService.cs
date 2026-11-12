using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Auditing;
using Reconciliation.Application.Common;
using Reconciliation.Application.Transactions;
using Reconciliation.Application.Webhooks;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.Enums;

namespace Reconciliation.Application.Matching;

public record ManualAllocation(Guid InvoiceId, decimal Amount);

public record ManualMatchRequest(IReadOnlyList<ManualAllocation> Allocations, string? Note);

public record ReasonRequest(string Reason);

public class ManualMatchRequestValidator : AbstractValidator<ManualMatchRequest>
{
    public ManualMatchRequestValidator()
    {
        RuleFor(x => x.Allocations).NotEmpty().Must(a => a.Count <= 20).WithMessage("At most 20 invoices per match.");
        RuleFor(x => x.Allocations)
            .Must(a => a.Select(i => i.InvoiceId).Distinct().Count() == a.Count)
            .WithMessage("Each invoice can appear only once.");
        RuleForEach(x => x.Allocations).ChildRules(allocation =>
        {
            allocation.RuleFor(a => a.InvoiceId).NotEmpty();
            allocation.RuleFor(a => a.Amount).ValidMoney();
        });
        RuleFor(x => x.Note).MaximumLength(400);
    }
}

public class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(400);
    }
}

/// <summary>
/// Changes a person makes by hand. Runs under the same lock as automatic reconciliation,
/// so a person and the engine never update the same invoice at the same moment.
/// </summary>
public class ManualMatchingService(
    IApplicationDbContext db,
    IDistributedLock distributedLock,
    ICurrentUser currentUser,
    AuditTrail audit,
    WebhookOutbox outbox,
    TimeProvider clock,
    IValidator<ManualMatchRequest> matchValidator,
    IValidator<ReasonRequest> reasonValidator)
{
    private static readonly TimeSpan LockExpiry = TimeSpan.FromMinutes(1);

    public async Task<IReadOnlyList<MatchResponse>> MatchAsync(Guid transactionId, ManualMatchRequest request, CancellationToken ct)
    {
        await matchValidator.ValidateAndThrowAsync(request, ct);
        await using var handle = await AcquireAsync(ct);

        var transaction = await FindTransactionAsync(transactionId, ct);
        var invoiceIds = request.Allocations.Select(a => a.InvoiceId).ToList();
        var invoices = await db.Invoices.Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var matches = new List<Match>();
        foreach (var allocation in request.Allocations)
        {
            var invoice = invoices.GetValueOrDefault(allocation.InvoiceId) ?? throw new NotFoundException("Invoice", allocation.InvoiceId);
            var reason = string.IsNullOrWhiteSpace(request.Note) ? "Manual match" : $"Manual match: {request.Note.Trim()}";

            // Domain rules (open amount, currency, direction) throw DomainException, and nothing is saved.
            var match = Match.Create(transaction, invoice, allocation.Amount, 100, reason, MatchMethod.Manual,
                currentUser.UserName, clock.GetUtcNow());
            db.Matches.Add(match);
            matches.Add(match);
            outbox.MatchCreated(match);

            audit.Record("match.created", "Match", match.Id, new
            {
                TransactionId = transaction.Id,
                InvoiceNumber = invoice.Number,
                match.Amount,
                Method = MatchMethod.Manual.ToString(),
                request.Note
            });
        }

        await db.SaveChangesAsync(ct);
        return matches.Select(MatchResponse.From).ToList();
    }

    /// <summary>Removes a match and restores both amounts. The transaction is then left to people, not the engine.</summary>
    public async Task ReverseAsync(Guid matchId, ReasonRequest request, CancellationToken ct)
    {
        await reasonValidator.ValidateAndThrowAsync(request, ct);
        await using var handle = await AcquireAsync(ct);

        var match = await db.Matches
            .Include(m => m.Transaction)
            .Include(m => m.Invoice)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct) ?? throw new NotFoundException("Match", matchId);

        match.Reverse();
        match.Transaction.ExcludeFromAutomaticMatching();
        db.Matches.Remove(match);
        outbox.MatchReversed(match, request.Reason);

        audit.Record("match.reversed", "Match", match.Id, new
        {
            match.TransactionId,
            InvoiceNumber = match.Invoice.Number,
            match.Amount,
            OriginalMethod = match.Method.ToString(),
            OriginalScore = match.Score,
            request.Reason
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<TransactionResponse> IgnoreAsync(Guid transactionId, ReasonRequest request, CancellationToken ct)
    {
        await reasonValidator.ValidateAndThrowAsync(request, ct);
        await using var handle = await AcquireAsync(ct);

        var transaction = await FindTransactionAsync(transactionId, ct);
        transaction.Ignore();
        audit.Record("transaction.ignored", "BankTransaction", transaction.Id, new { request.Reason });
        await db.SaveChangesAsync(ct);

        return TransactionResponse.From(transaction, currentUser.IsAdmin);
    }

    public async Task<TransactionResponse> RestoreAsync(Guid transactionId, CancellationToken ct)
    {
        await using var handle = await AcquireAsync(ct);

        var transaction = await FindTransactionAsync(transactionId, ct);
        transaction.Restore();
        audit.Record("transaction.restored", "BankTransaction", transaction.Id);
        await db.SaveChangesAsync(ct);

        return TransactionResponse.From(transaction, currentUser.IsAdmin);
    }

    private async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct) =>
        await distributedLock.TryAcquireAsync(ReconciliationService.LockName, LockExpiry, ct)
        ?? throw new ConflictException("Automatic reconciliation is running. Try again in a moment.");

    private async Task<BankTransaction> FindTransactionAsync(Guid id, CancellationToken ct) =>
        await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Transaction", id);
}
