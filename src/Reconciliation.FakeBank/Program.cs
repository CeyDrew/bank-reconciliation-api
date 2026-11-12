using Reconciliation.FakeBank.Chaos;
using Reconciliation.FakeBank.Statements;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<StatementOptions>(builder.Configuration.GetSection(StatementOptions.SectionName));
builder.Services.Configure<ChaosOptions>(builder.Configuration.GetSection(ChaosOptions.SectionName));
builder.Services.AddSingleton<StatementGenerator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ChaosMiddleware>();

app.MapGet("/", () => "FakeBank: simulated bank API for local development.");
app.MapHealthChecks("/health");

// Statement of one account, oldest first, paged.
app.MapGet("/api/v1/accounts/{iban}/transactions",
    (string iban, DateOnly from, int? page, int? pageSize, StatementGenerator generator) =>
        generator.GetPage(iban.ToUpperInvariant(), from, page ?? 1, pageSize ?? 50));

app.Run();
