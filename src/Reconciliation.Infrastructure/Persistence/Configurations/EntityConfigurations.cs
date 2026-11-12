using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reconciliation.Domain.Entities;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Infrastructure.Persistence.Configurations;

// Financial records are never deleted by cascade: every relationship uses DeleteBehavior.Restrict.

internal class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.TaxNumber)
            .HasConversion(t => t.Value, value => TaxNumber.Create(value))
            .HasMaxLength(10).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.Iban).IsOptionalIban();
        builder.Property(c => c.Email).HasMaxLength(254);

        builder.HasIndex(c => c.TaxNumber).IsUnique();
        builder.HasIndex(c => c.Iban);
    }
}

internal class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.Iban).IsIban();
        builder.Property(a => a.Currency).IsCurrency();

        builder.HasIndex(a => a.Iban).IsUnique();
    }
}

internal class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.Property(i => i.Number).HasMaxLength(50);
        builder.Property(i => i.Currency).IsCurrency();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => new { i.CustomerId, i.Status });
    }
}

internal class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("BankTransactions");

        builder.Property(t => t.ExternalId).HasMaxLength(100).IsUnicode(false);
        builder.Property(t => t.Currency).IsCurrency();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.CounterpartyName).HasMaxLength(200);
        builder.Property(t => t.CounterpartyIban).HasMaxLength(34).IsUnicode(false);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(t => t.Account).WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Same bank movement can't be imported twice.
        builder.HasIndex(t => new { t.AccountId, t.ExternalId }).IsUnique();
        builder.HasIndex(t => new { t.Status, t.BookingDate });
    }
}

internal class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.CreatedBy).HasMaxLength(100);

        builder.HasOne(m => m.Transaction).WithMany().HasForeignKey(m => m.TransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Invoice).WithMany().HasForeignKey(m => m.InvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("CK_Matches_Score", "[Score] BETWEEN 0 AND 100"));
        builder.ToTable(t => t.HasCheckConstraint("CK_Matches_Amount", "[Amount] > 0"));
    }
}
