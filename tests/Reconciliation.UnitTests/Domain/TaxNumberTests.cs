using FluentAssertions;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.UnitTests.Domain;

public class TaxNumberTests
{
    [Theory]
    [InlineData("1234567890")]
    [InlineData("2345678909")]
    [InlineData(" 4567890128 ")]
    public void Valid_tax_number_is_accepted(string input)
    {
        TaxNumber.IsValid(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("1234567891")]  // wrong check digit
    [InlineData("123456789")]   // too short
    [InlineData("12345678901")] // 11 digits is a personal ID number, not a VKN
    [InlineData("12345A7890")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_tax_number_is_rejected(string? input)
    {
        TaxNumber.IsValid(input).Should().BeFalse();
    }

    [Fact]
    public void Create_trims_value()
    {
        TaxNumber.Create(" 1234567890 ").Value.Should().Be("1234567890");
    }
}
