using Microsoft.EntityFrameworkCore;
using Reconciliation.Application.Abstractions;
using Reconciliation.Infrastructure.Persistence;

namespace Reconciliation.UnitTests;

internal sealed class StubCurrentUser(string userName = "tester@example.com", bool isAdmin = false, Guid? userId = null) : ICurrentUser
{
    public Guid? UserId { get; } = userId ?? Guid.NewGuid();
    public string UserName { get; } = userName;
    public bool IsAdmin { get; } = isAdmin;
}

internal static class TestDb
{
    /// <summary>A fresh in-memory database per test.</summary>
    public static AppDbContext Create() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
