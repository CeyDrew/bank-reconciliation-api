using Microsoft.Extensions.DependencyInjection;

namespace Reconciliation.Application;

public static class DependencyInjection
{
    /// <summary>Registers application-layer services (use cases, validators, matching engine).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
