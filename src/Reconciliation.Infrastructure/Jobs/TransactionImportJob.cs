using Quartz;
using Reconciliation.Application.Transactions;

namespace Reconciliation.Infrastructure.Jobs;

public class TransactionImportJobOptions
{
    public const string SectionName = "Jobs:TransactionImport";

    public bool Enabled { get; init; }
    public int IntervalSeconds { get; init; } = 300;
}

/// <summary>
/// Scheduled by Quartz. DisallowConcurrentExecution stops overlapping runs inside one instance;
/// the Redis lock in <see cref="TransactionImportService"/> stops them across instances.
/// </summary>
[DisallowConcurrentExecution]
public class TransactionImportJob(TransactionImportService importService) : IJob
{
    public static readonly JobKey Key = new("transaction-import");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await importService.RunAsync(cancellationToken);
}
