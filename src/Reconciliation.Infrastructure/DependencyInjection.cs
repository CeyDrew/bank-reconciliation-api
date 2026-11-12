using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Quartz;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Matching;
using Reconciliation.Application.Transactions;
using Reconciliation.Application.Webhooks;
using Reconciliation.Infrastructure.Auth;
using Reconciliation.Infrastructure.Banking;
using Reconciliation.Infrastructure.Jobs;
using Reconciliation.Infrastructure.Locking;
using Reconciliation.Infrastructure.Persistence;
using Reconciliation.Infrastructure.Webhooks;
using StackExchange.Redis;

namespace Reconciliation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers infrastructure services (EF Core, Redis, bank client, webhooks).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddAuth(configuration);
        services.AddLocking(configuration);
        services.AddBankClient(configuration);
        services.AddWebhooks(configuration);
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

    private static void AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Fails at startup, not at the first login, when the signing key is missing or too short.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<SeedUserOptions>(configuration.GetSection(SeedUserOptions.SectionName));

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
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
        services.Configure<ReconciliationOptions>(configuration.GetSection(ReconciliationOptions.SectionName));

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

    private static void AddWebhooks(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebhookDispatchOptions>(configuration.GetSection(WebhookDispatchOptions.SectionName));
        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => !o.Enabled || o.Secret.Length >= 32,
                "Webhooks:Secret must be at least 32 characters when Webhooks:Url is set. Set WEBHOOK_SECRET in .env.")
            .ValidateOnStart();

        services
            .AddHttpClient<IWebhookSender, HttpWebhookSender>()
            // Retrying a POST is safe here only because every event carries an idempotency key.
            // A few quick retries smooth over blips; longer outages are handled by the outbox's own backoff.
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            });
    }

    private static void AddJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var bankSync = configuration.GetSection(BankSyncJobOptions.SectionName).Get<BankSyncJobOptions>() ?? new BankSyncJobOptions();
        var webhooks = configuration.GetSection(WebhookOptions.SectionName).Get<WebhookOptions>() ?? new WebhookOptions();
        var dispatchJob = webhooks.Enabled && webhooks.AutoDispatch;
        if (!bankSync.Enabled && !dispatchJob)
        {
            return;
        }

        services.AddQuartz(quartz =>
        {
            if (bankSync.Enabled)
            {
                AddRepeatingJob<BankSyncJob>(quartz, BankSyncJob.Key, TimeSpan.FromSeconds(bankSync.IntervalSeconds));
            }

            if (dispatchJob)
            {
                AddRepeatingJob<WebhookDispatchJob>(quartz, WebhookDispatchJob.Key, TimeSpan.FromSeconds(webhooks.DispatchIntervalSeconds));
            }
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
    }

    private static void AddRepeatingJob<TJob>(IQuartzBuilder quartz, JobKey key, TimeSpan interval) where TJob : IJob
    {
        quartz.AddJob<TJob>(job => job.WithIdentity(key));
        quartz.AddTrigger(trigger => trigger
            .ForJob(key)
            .WithIdentity($"{key.Name}-trigger")
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(interval).RepeatForever()));
    }
}
