using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reconciliation.Application.Abstractions;

namespace Reconciliation.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending migrations when enabled. Demo data is seeded as part of the migration
    /// (see UseAsyncSeeding in <see cref="DependencyInjection"/>); users are seeded here because
    /// their passwords need the hasher and configuration.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.MigrateOnStartup)
        {
            return;
        }

        var logger = provider.GetRequiredService<ILogger<AppDbContext>>();
        var db = provider.GetRequiredService<AppDbContext>();

        logger.LogInformation("Applying database migrations");
        await db.Database.MigrateAsync(ct);

        if (options.Seed)
        {
            await UserSeeder.SeedAsync(db, provider.GetRequiredService<IPasswordHasher>(),
                provider.GetRequiredService<IOptions<SeedUserOptions>>().Value, provider.GetRequiredService<TimeProvider>(),
                logger, ct);
        }
    }
}
