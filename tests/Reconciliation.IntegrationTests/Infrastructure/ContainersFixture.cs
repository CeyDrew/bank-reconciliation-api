using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Reconciliation.IntegrationTests.Infrastructure;

/// <summary>
/// Real SQL Server and Redis in Docker, started once for all integration tests.
/// Testcontainers picks random host ports and a generated password, so nothing clashes with a local setup.
/// </summary>
public class ContainersFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>A connection string to a new, empty database, so every test class starts from a clean state.</summary>
    public string NewDatabaseConnectionString() =>
        new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = $"recon_{Guid.NewGuid():N}",
            TrustServerCertificate = true
        }.ConnectionString;

    public Task InitializeAsync() => Task.WhenAll(_sql.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}
