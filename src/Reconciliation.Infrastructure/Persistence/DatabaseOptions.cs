namespace Reconciliation.Infrastructure.Persistence;

/// <summary>Bound from the "Database" configuration section.</summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations at startup. Convenient for development and Docker Compose only.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Insert demo data when the database is empty.</summary>
    public bool Seed { get; init; }
}
