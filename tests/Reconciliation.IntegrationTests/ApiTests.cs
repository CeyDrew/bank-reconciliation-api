using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Reconciliation.IntegrationTests.Infrastructure;

namespace Reconciliation.IntegrationTests;

/// <summary>HTTP behaviour over a real database: roles, masking, validation, audit.</summary>
[Collection(ContainersCollection.Name)]
public class ApiTests(ContainersFixture containers) : IntegrationTest(containers)
{
    [Fact]
    public async Task Iban_is_masked_for_accountants_and_shown_to_admins()
    {
        var accountant = await (await App.AsAccountant()).GetStringAsync("/api/v1/customers?search=Acme");
        var admin = await (await App.AsAdmin()).GetStringAsync("/api/v1/customers?search=Acme");

        accountant.Should().Contain("TR71******************0011").And.NotContain("TR719999100000002000000011");
        admin.Should().Contain("TR719999100000002000000011");
    }

    [Theory]
    [InlineData("POST", "/api/v1/accounts")]
    [InlineData("GET", "/api/v1/audit-log")]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("GET", "/api/v1/webhooks/deliveries")]
    public async Task Accountant_cannot_use_admin_endpoints(string method, string url)
    {
        var client = await App.AsAccountant();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { })
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_lifecycle_with_validation_conflicts_and_audit()
    {
        var accountant = await App.AsAccountant();

        var invalid = await accountant.PostAsJsonAsync("/api/v1/customers", new { name = "Wayne Metals", taxNumber = "5678901234" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("not a valid 10-digit tax number");

        var created = await accountant.PostAsJsonAsync("/api/v1/customers", new { name = "Wayne Metals", taxNumber = "5678901233" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await accountant.PostAsJsonAsync("/api/v1/customers", new { name = "Other", taxNumber = "5678901233" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var audit = await (await App.AsAdmin()).GetStringAsync("/api/v1/audit-log?action=customer.created");
        audit.Should().Contain("accountant@example.com").And.Contain("Wayne Metals");
    }

    [Fact]
    public async Task Wrong_password_gets_401_and_report_rejects_long_ranges()
    {
        var login = await App.Anonymous().PostAsJsonAsync("/api/v1/auth/login",
            new { email = TestApp.AdminEmail, password = "definitely wrong" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var report = await (await App.AsAccountant()).GetAsync("/api/v1/reports/reconciliation?from=2026-01-01&to=2026-12-31");
        report.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
