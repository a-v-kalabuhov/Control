using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ProductArticleTests
{
    [Theory]
    [InlineData("  a123 ", "A123")]
    [InlineData("blk", "BLK")]
    [InlineData("123.а", "123.А")]   // кириллица
    [InlineData(null, "")]
    public void Normalize_TrimsAndUppercases(string? input, string expected) =>
        ProductArticle.Normalize(input).Should().Be(expected);

    [Fact]
    public void Compose_JoinsWithDotAndNormalizes() =>
        ProductArticle.Compose(" a123", "blk ").Should().Be("A123.BLK");

    [Fact]
    public void Compose_DefaultPart() =>
        ProductArticle.Compose("A123", ProductArticle.DefaultPart).Should().Be("A123.0");

    [Fact]
    public void Compose_DotsInsideParts_CanCollide()
    {
        // Коллизия из спеки (решение 4): обе пары дают один полный артикул — её ловит проверка уникальности.
        ProductArticle.Compose("123.А", "0").Should().Be(ProductArticle.Compose("123", "А.0"));
    }
}
