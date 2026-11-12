using FluentValidation;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Application.Common;

/// <summary>Reusable FluentValidation rules backed by the domain's own checks.</summary>
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, string?> ValidIban<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(Iban.IsValid).WithMessage("'{PropertyName}' is not a valid IBAN.");

    public static IRuleBuilderOptions<T, string> ValidTaxNumber<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(TaxNumber.IsValid).WithMessage("'{PropertyName}' is not a valid 10-digit tax number (VKN).");

    public static IRuleBuilderOptions<T, string> ValidCurrency<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(Currency.IsValid).WithMessage("'{PropertyName}' must be a 3-letter upper-case currency code.");

    public static IRuleBuilderOptions<T, decimal> ValidMoney<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
}
