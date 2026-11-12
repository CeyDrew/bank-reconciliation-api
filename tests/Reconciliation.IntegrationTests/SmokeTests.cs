using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reconciliation.IntegrationTests;

/// <summary>The API with a throw-away signing key; no database is needed for these checks.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + new string('x', 32));
    }
}

public class SmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v1/info")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Public_endpoint_returns_ok(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/customers")]
    [InlineData("/api/v1/transactions")]
    [InlineData("/api/v1/audit-log")]
    public async Task Protected_endpoint_without_token_returns_401(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forged_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            "eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIiwicm9sZSI6IkFkbWluIn0."); // alg "none", role Admin

        var response = await client.GetAsync("/api/v1/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
