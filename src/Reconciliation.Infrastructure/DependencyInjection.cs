using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reconciliation.Application.Abstractions;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services (EF Core, Redis, bank client, webhooks).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSection = configuration.GetSection(DatabaseOptions.SectionName);
        services.Configure<DatabaseOptions>(databaseSection);
        var seed = databaseSection.Get<DatabaseOptions>()?.Seed ?? false;

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 5));

            if (seed)
            {
                // EF Core runs these after Migrate(); the sync version is used by "dotnet ef database update".
                options.UseAsyncSeeding((context, _, ct) => DataSeeder.SeedAsync(context, ct));
                options.UseSeeding((context, _) => DataSeeder.SeedAsync(context, CancellationToken.None).GetAwaiter().GetResult());
            }
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        return services;
    }
}
