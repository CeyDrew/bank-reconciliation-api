using FluentAssertions;
using Reconciliation.Application.Common;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.UnitTests.Application;

public class MaskingTests
{
    [Theory]
    [InlineData("TR719999100000002000000011", false, "TR71******************0011")]
    [InlineData("TR719999100000002000000011", true, "TR719999100000002000000011")]
    [InlineData("SHORT", false, "*****")]
    [InlineData(null, false, null)]
    public void Raw_iban_is_masked_unless_revealed(string? input, bool reveal, string? expected)
    {
        Masking.Iban(input, reveal).Should().Be(expected);
    }

    [Fact]
    public void Iban_value_object_is_masked_unless_revealed()
    {
        var iban = Iban.Create("TR719999100000002000000011");

        Masking.Iban(iban, reveal: false).Should().Be("TR71******************0011");
        Masking.Iban(iban, reveal: true).Should().Be(iban.Value);
        Masking.Iban((Iban?)null, reveal: true).Should().BeNull();
    }
}
