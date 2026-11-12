using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Reconciliation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services (EF Core, Redis, bank client, webhooks).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
