using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.Infrastructure.Persistence.Configurations;

/// <summary>Column mappings shared by several entities.</summary>
internal static class ConverterExtensions
{
    public static PropertyBuilder<Iban> IsIban(this PropertyBuilder<Iban> property) =>
        property.HasConversion(iban => iban.Value, value => Iban.Create(value)).HasMaxLength(34).IsUnicode(false);

    public static PropertyBuilder<Iban?> IsOptionalIban(this PropertyBuilder<Iban?> property) =>
        property.HasConversion(iban => iban!.Value, value => Iban.Create(value)).HasMaxLength(34).IsUnicode(false);

    public static PropertyBuilder<string> IsCurrency(this PropertyBuilder<string> property) =>
        property.HasMaxLength(3).IsFixedLength().IsUnicode(false);
}
