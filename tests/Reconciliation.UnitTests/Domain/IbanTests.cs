using FluentAssertions;
using Reconciliation.Domain.Common;
using Reconciliation.Domain.ValueObjects;

namespace Reconciliation.UnitTests.Domain;

public class IbanTests
{
    [Theory]
    [InlineData("TR549999000000001000000001")]
    [InlineData("tr54 9999 0000 0000 1000 0000 01")] // lower case and spaces are normalised
    [InlineData("GB82WEST12345698765432")]           // ISO 13616 example
    public void Valid_iban_is_accepted(string input)
    {
        Iban.IsValid(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("TR549999000000001000000002")] // wrong check digits
    [InlineData("TR54999900000000100000000")]  // Turkish IBAN must be 26 characters
    [InlineData("1R549999000000001000000001")] // country code must be letters
    [InlineData("TR54-9999-0000")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_iban_is_rejected(string? input)
    {
        Iban.IsValid(input).Should().BeFalse();
    }

    [Fact]
    public void Create_normalises_value()
    {
        Iban.Create("tr54 9999 0000 0000 1000 0000 01").Value.Should().Be("TR549999000000001000000001");
    }

    [Fact]
    public void Create_throws_for_invalid_iban()
    {
        var act = () => Iban.Create("TR000000");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void FromBban_calculates_check_digits()
    {
        Iban.FromBban("TR", "9999000000001000000001").Value.Should().Be("TR549999000000001000000001");
    }

    [Fact]
    public void Masked_hides_the_middle()
    {
        Iban.Create("TR549999000000001000000001").Masked.Should().Be("TR54******************0001");
    }

    [Fact]
    public void Ibans_with_same_value_are_equal()
    {
        Iban.Create("TR549999000000001000000001").Should().Be(Iban.Create("TR54 9999 0000 0000 1000 0000 01"));
    }
}
