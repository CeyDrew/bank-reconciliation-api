using Quartz;
using Reconciliation.Application.Webhooks;

namespace Reconciliation.Infrastructure.Jobs;

/// <summary>Sends pending outbox events to the ERP every few seconds.</summary>
[DisallowConcurrentExecution]
public class WebhookDispatchJob(WebhookDispatcher dispatcher) : IJob
{
    public static readonly JobKey Key = new("webhook-dispatch");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await dispatcher.DispatchAsync(cancellationToken);
}
