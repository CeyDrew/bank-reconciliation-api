using Microsoft.EntityFrameworkCore;
using Reconciliation.Domain.Entities;

namespace Reconciliation.Application.Abstractions;

/// <summary>
/// The database as seen by the application layer. Implemented by the EF Core context in Infrastructure,
/// so use cases don't depend on SQL Server or any other concrete provider.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<BankTransaction> BankTransactions { get; }
    DbSet<Match> Matches { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
