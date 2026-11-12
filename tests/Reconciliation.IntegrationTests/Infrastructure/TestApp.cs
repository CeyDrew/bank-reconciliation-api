extern alias FakeBankApp;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reconciliation.Application.Abstractions;
using Reconciliation.Application.Webhooks;
using FakeBankApp::Reconciliation.FakeBank.Statements;
using Reconciliation.Infrastructure.Banking;
using Reconciliation.Infrastructure.Persistence;
using Reconciliation.Infrastructure.Webhooks;

namespace Reconciliation.IntegrationTests.Infrastructure;

/// <summary>
/// The whole system in one process: the API on its own fresh SQL Server database plus Redis,
/// talking over in-memory HTTP to the FakeBank (bank API and ERP receiver).
/// Scheduled jobs are off; tests trigger import, reconciliation and dispatch through the API.
/// </summary>
public sealed class TestApp : IAsyncDisposable
{
    public const string AdminEmail = "admin@example.com";
    public const string AccountantEmail = "accountant@example.com";

    // Generated per run: test-only values, never real secrets.
    private readonly string _adminPassword = "admin-" + Guid.NewGuid();
    private readonly string _accountantPassword = "accountant-" + Guid.NewGuid();
    private readonly string _webhookSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, string> _tokens = [];

    public WebApplicationFactory<StatementGenerator> FakeBank { get; }
    public WebApplicationFactory<Program> Api { get; }

    public TestApp(ContainersFixture containers)
    {
        FakeBank = new WebApplicationFactory<StatementGenerator>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Statements:NoiseProbability", "0"); // only the 11 scripted movements
            builder.UseSetting("Erp:WebhookSecret", _webhookSecret);
        });

        var connectionString = containers.NewDatabaseConnectionString();
        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:Redis", containers.RedisConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("Jwt:SigningKey", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            builder.UseSetting("SeedUsers:AdminPassword", _adminPassword);
            builder.UseSetting("SeedUsers:AccountantPassword", _accountantPassword);
            builder.UseSetting("FakeBank:BaseUrl", "http://fakebank/");
            builder.UseSetting("Webhooks:Url", "http://fakebank/erp/webhooks");
            builder.UseSetting("Webhooks:Secret", _webhookSecret);
            builder.UseSetting("Webhooks:AutoDispatch", "false");

            builder.ConfigureTestServices(services =>
            {
                // Same typed clients and resilience pipelines, but the bytes go to the in-memory FakeBank.
                services.AddHttpClient<IBankClient, FakeBankClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => FakeBank.Server.CreateHandler());
                services.AddHttpClient<IWebhookSender, HttpWebhookSender>()
                    .ConfigurePrimaryHttpMessageHandler(() => FakeBank.Server.CreateHandler());
            });
        });
    }

    public HttpClient Anonymous() => Api.CreateClient();

    public Task<HttpClient> AsAdmin() => SignedIn(AdminEmail, _adminPassword);

    public Task<HttpClient> AsAccountant() => SignedIn(AccountantEmail, _accountantPassword);

    /// <summary>A database context on the API's database, for checks the HTTP API doesn't expose.</summary>
    public AsyncServiceScope CreateScope(out AppDbContext db)
    {
        var scope = Api.Services.CreateAsyncScope();
        db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return scope;
    }

    private async Task<HttpClient> SignedIn(string email, string password)
    {
        // Logins are rate limited, so each user signs in once per app.
        if (!_tokens.TryGetValue(email, out var token))
        {
            var response = await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;
            _tokens[email] = token;
        }

        var client = Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Api.DisposeAsync();
        await FakeBank.DisposeAsync();
    }

    private record TokenDto(string AccessToken);
}

/// <summary>Base class: one fresh app (and database) per test class.</summary>
[Collection(ContainersCollection.Name)]
public abstract class IntegrationTest(ContainersFixture containers) : IAsyncLifetime
{
    protected TestApp App { get; private set; } = null!;

    public Task InitializeAsync()
    {
        App = new TestApp(containers);
        _ = App.Api.Server; // start now: migrations and seeding run here, not inside the first test
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await App.DisposeAsync();
}
