using Microsoft.Extensions.Options;

namespace Reconciliation.FakeBank.Chaos;

public class ChaosOptions
{
    public const string SectionName = "Chaos";

    /// <summary>Share of API requests (0-1) answered with 503, to exercise the client's retries.</summary>
    public double FailureRate { get; init; }

    /// <summary>Upper bound of a random delay added to every API request.</summary>
    public int MaxLatencyMs { get; init; }
}

/// <summary>Makes the bank unreliable on purpose: random latency and random 503 responses.</summary>
public class ChaosMiddleware(RequestDelegate next, IOptionsMonitor<ChaosOptions> options, ILogger<ChaosMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var chaos = options.CurrentValue;
        if (chaos.MaxLatencyMs > 0)
        {
            await Task.Delay(Random.Shared.Next(chaos.MaxLatencyMs), context.RequestAborted);
        }

        if (Random.Shared.NextDouble() < chaos.FailureRate)
        {
            logger.LogInformation("Chaos: failing {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Simulated outage", context.RequestAborted);
            return;
        }

        await next(context);
    }
}
