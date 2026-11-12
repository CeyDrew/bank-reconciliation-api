using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Quartz;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Transactions;
using Reconciliation.Infrastructure.Banking;
using Reconciliation.Infrastructure.Jobs;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Reconciliation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services (EF Core, Redis, bank client, webhooks).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddLocking(configuration);
        services.AddBankClient(configuration);
        services.AddJobs(configuration);

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
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
    }

    private static void AddLocking(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
            return;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false; // start even if Redis is down; it reconnects in the background
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
    }

    private static void AddBankClient(this IServiceCollection services, IConfiguration configuration)
    {
        var bankOptions = configuration.GetSection(BankApiOptions.SectionName).Get<BankApiOptions>() ?? new BankApiOptions();
        services.Configure<TransactionImportOptions>(configuration.GetSection(TransactionImportOptions.SectionName));

        services
            .AddHttpClient<IBankClient, FakeBankClient>(client =>
            {
                if (!string.IsNullOrWhiteSpace(bankOptions.BaseUrl))
                {
                    client.BaseAddress = new Uri(bankOptions.BaseUrl.TrimEnd('/') + "/");
                }
            })
            // Polly v8 pipeline: total timeout > retry (exponential backoff + jitter) > circuit breaker > attempt timeout.
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 4;
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });
    }

    private static void AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var jobOptions = configuration.GetSection(TransactionImportJobOptions.SectionName).Get<TransactionImportJobOptions>()
            ?? new TransactionImportJobOptions();
        if (!jobOptions.Enabled)
        {
            return;
        }

        services.AddQuartz(quartz =>
        {
            quartz.AddJob<TransactionImportJob>(job => job.WithIdentity(TransactionImportJob.Key));
            quartz.AddTrigger(trigger => trigger
                .ForJob(TransactionImportJob.Key)
                .WithIdentity("transaction-import-trigger")
                .StartNow()
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(TimeSpan.FromSeconds(jobOptions.IntervalSeconds))
                    .RepeatForever()));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
    }
}
