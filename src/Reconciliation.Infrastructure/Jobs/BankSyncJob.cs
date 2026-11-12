using Quartz;
using Reconciliation.Application.Matching;
using Reconciliation.Application.Transactions;

namespace Reconciliation.Infrastructure.Jobs;

public class BankSyncJobOptions
{
    public const string SectionName = "Jobs:BankSync";

    public bool Enabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;
}

/// <summary>
/// Scheduled by Quartz: imports new bank transactions, then matches them to invoices.
/// DisallowConcurrentExecution stops overlapping runs inside one instance;
/// the Redis locks in the two services stop them across instances.
/// </summary>
[DisallowConcurrentExecution]
public class BankSyncJob(TransactionImportService importService, ReconciliationService reconciliationService) : IJob
{
    public static readonly JobKey Key = new("bank-sync");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await importService.RunAsync(cancellationToken);
        await reconciliationService.RunAsync(cancellationToken);
    }
}
