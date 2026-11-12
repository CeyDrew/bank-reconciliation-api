namespace Reconciliation.Domain.Common;

public static class Currency
{
    public const string TurkishLira = "TRY";

    /// <summary>ISO 4217 style code: exactly three upper-case letters.</summary>
    public static bool IsValid(string? code) =>
        code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);

    public static string Ensure(string code) =>
        IsValid(code) ? code : throw new DomainException($"'{code}' is not a valid currency code.");
}
