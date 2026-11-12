using Microsoft.Extensions.Options;
using Reconciliation.FakeBank.Chaos;
using Reconciliation.FakeBank.Erp;
using Reconciliation.FakeBank.Statements;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<StatementOptions>(builder.Configuration.GetSection(StatementOptions.SectionName));
builder.Services.Configure<ChaosOptions>(builder.Configuration.GetSection(ChaosOptions.SectionName));
builder.Services.Configure<ErpOptions>(builder.Configuration.GetSection(ErpOptions.SectionName));
builder.Services.AddSingleton<StatementGenerator>();
builder.Services.AddSingleton<WebhookVerifier>();
builder.Services.AddSingleton<ErpInbox>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ChaosMiddleware>();

app.MapGet("/", () => "FakeBank: simulated bank API and ERP webhook receiver for local development.");
app.MapHealthChecks("/health");

// Bank: statement of one account, oldest first, paged.
app.MapGet("/api/v1/accounts/{iban}/transactions",
    (string iban, DateOnly from, int? page, int? pageSize, StatementGenerator generator) =>
        generator.GetPage(iban.ToUpperInvariant(), from, page ?? 1, pageSize ?? 50));

// ERP: receives signed webhooks from the reconciliation API.
app.MapPost("/erp/webhooks", async (HttpRequest request, WebhookVerifier verifier, ErpInbox inbox,
    IOptions<ErpOptions> options, TimeProvider clock, ILogger<ErpInbox> logger) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var verification = verifier.Verify(request.Headers["X-Webhook-Timestamp"], request.Headers["X-Webhook-Signature"], body);
    if (verification != VerificationResult.Valid)
    {
        logger.LogWarning("ERP: rejected webhook ({Reason})", verification);
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: verification.ToString());
    }

    var id = request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrEmpty(id))
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Idempotency-Key header is required");
    }

    if (!inbox.TryAccept(new ReceivedWebhook(id, request.Headers["X-Webhook-Event"].ToString(), clock.GetUtcNow(), body)))
    {
        inbox.CountDuplicate();
        logger.LogInformation("ERP: duplicate webhook {Id} ignored", id);
        return Results.Ok(new { id, duplicate = true });
    }

    logger.LogInformation("ERP: booked webhook {Id}", id);
    if (Random.Shared.NextDouble() < options.Value.FailAfterAcceptRate)
    {
        // Booked, but the sender never hears about it: it will retry.
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { id, duplicate = false });
});

app.MapGet("/erp/webhooks", (ErpInbox inbox) => inbox.Summary());

app.Run();
