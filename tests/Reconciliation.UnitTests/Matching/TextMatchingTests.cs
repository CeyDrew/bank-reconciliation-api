using FluentAssertions;
using Reconciliation.Application.Matching;

namespace Reconciliation.UnitTests.Matching;

public class TextMatchingTests
{
    [Theory]
    [InlineData("inv-2026/0008", "INV20260008")]
    [InlineData("  Ödeme: Şirket fatura ", "ODEMESIRKETFATURA")]
    [InlineData(null, "")]
    public void Normalize_keeps_upper_case_letters_and_digits(string? input, string expected)
    {
        TextMatching.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("PAYMENTINV20260001", "INV20260001", true)]
    [InlineData("INV202600012", "INV20260001", false)] // longer number
    [InlineData("X20260001Y", "20260001", true)]
    [InlineData("120260001", "20260001", false)]       // digit before a numeric token
    [InlineData("INV20260001INV20260001", "INV20260001", true)]
    [InlineData("ANYTHING", "", false)]
    public void ContainsToken_respects_number_boundaries(string haystack, string token, bool expected)
    {
        TextMatching.ContainsToken(haystack, token).Should().Be(expected);
    }

    [Fact]
    public void ContainsToken_finds_a_later_occurrence_when_the_first_is_part_of_a_longer_number()
    {
        TextMatching.ContainsToken("INV202600019INV20260001", "INV20260001").Should().BeTrue();
    }

    [Theory]
    [InlineData("ACME TEXTILES LTD", "Acme Textiles Ltd.", true)]
    [InlineData("Örnek Gıda San. ve Tic. A.Ş.", "ÖRNEK GIDA", true)]
    [InlineData("İstanbul Çelik Ltd. Şti.", "ISTANBUL CELIK LTD STI", true)]
    [InlineData("ACME TEXTILES", "ACME FOODS", false)]
    [InlineData("LTD", "LTD", false)] // nothing left after removing the legal form
    [InlineData(null, "ACME", false)]
    public void SameCompanyName_ignores_case_punctuation_and_legal_forms(string? a, string? b, bool expected)
    {
        TextMatching.SameCompanyName(a, b).Should().Be(expected);
    }

    [Fact]
    public void TenDigitNumbers_returns_stand_alone_ten_digit_numbers()
    {
        TextMatching.TenDigitNumbers("VKN 4567890128, ref 12345678901, id 123").Should().Equal("4567890128");
    }
}
