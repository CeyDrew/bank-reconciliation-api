using Microsoft.Extensions.Options;

namespace Reconciliation.FakeBank.Statements;

public class StatementOptions
{
    public const string SectionName = "Statements";

    /// <summary>Length of a time slot. Each slot may produce one random movement.</summary>
    public int SlotMinutes { get; init; } = 10;

    /// <summary>Chance (0-1) that a slot produces a movement.</summary>
    public double NoiseProbability { get; init; } = 0.25;

    /// <summary>How far back random movements exist.</summary>
    public int NoiseHistoryHours { get; init; } = 24;
}

/// <summary>
/// Produces an account's statement without storing anything: random movements are derived from
/// (IBAN, time slot), so every call, and every restart, returns the same data for the same slot.
/// New movements "arrive" simply because time moves on.
/// </summary>
public class StatementGenerator(TimeProvider clock, IOptions<StatementOptions> options)
{
    public const int MaxPageSize = 100;

    private static readonly string[] Payers = ["NORTHWIND SUPPLIES", "CONTOSO OFFICE", "FABRIKAM PARTS", "TAILSPIN TOYS", "WOODGROVE TRADING"];
    private static readonly string[] Expenses = ["Electricity bill", "Supplier payment", "Card settlement", "Courier services", "Software subscription"];

    public StatementPage GetPage(string iban, DateOnly from, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var lines = GetLines(iban, from);
        var items = lines.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new StatementPage(items, page, pageSize, lines.Count, page * pageSize < lines.Count);
    }

    public IReadOnlyList<StatementLine> GetLines(string iban, DateOnly from)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        return ScenarioCatalog.For(iban, today)
            .Concat(RandomLines(iban, now))
            .Where(line => line.BookingDate >= from)
            .OrderBy(line => line.BookingDate)
            .ThenBy(line => line.TransactionId, StringComparer.Ordinal)
            .ToList();
    }

    private IEnumerable<StatementLine> RandomLines(string iban, DateTimeOffset now)
    {
        var settings = options.Value;
        var slotLength = TimeSpan.FromMinutes(settings.SlotMinutes);
        var lastSlot = now.ToUnixTimeSeconds() / (long)slotLength.TotalSeconds;
        var firstSlot = lastSlot - (long)(TimeSpan.FromHours(settings.NoiseHistoryHours) / slotLength);
        var accountSeed = StableHash(iban);

        for (var slot = firstSlot; slot <= lastSlot; slot++)
        {
            // HashCode.Combine is also randomised per process; mix the values by hand instead.
            var random = new Random(unchecked(accountSeed ^ (int)(slot * 2654435761)));
            if (random.NextDouble() >= settings.NoiseProbability)
            {
                continue;
            }

            var bookingDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(slot * (long)slotLength.TotalSeconds).UtcDateTime);
            yield return CreateRandomLine($"FB-{slot}", bookingDate, random);
        }
    }

    private static StatementLine CreateRandomLine(string id, DateOnly bookingDate, Random random)
    {
        var kind = random.Next(10);
        if (kind < 4)
        {
            var expense = Expenses[random.Next(Expenses.Length)];
            return new StatementLine(id, bookingDate, -Money(random, 50, 5_000), "TRY", expense, null, null);
        }

        var payer = Payers[random.Next(Payers.Length)];
        var payerIban = FakeIban.FromAccountNumber(5 + random.Next(5), random.NextInt64(1, 9_999_999_999_999_999));
        var description = kind < 8 ? "Transfer" : $"INV-2026-9{random.Next(1000):000}"; // unknown invoice numbers
        return new StatementLine(id, bookingDate, Money(random, 100, 20_000), "TRY", description, payer, payerIban);
    }

    private static decimal Money(Random random, int min, int max) => random.Next(min * 100, max * 100) / 100m;

    /// <summary>FNV-1a. string.GetHashCode() is randomised per process, so it can't be used for stable seeds.</summary>
    private static int StableHash(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash = (hash ^ c) * 16777619;
        }

        return unchecked((int)hash);
    }
}
