using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class MaterialArticleSuggesterTests
{
    [Theory]
    [InlineData("PP-0099-X7", "PP-0100-X7")]
    [InlineData("A12-B34", "A12-B35")]
    [InlineData("A999-B12", "A1000-B12")]
    [InlineData("ABC", "ABC0")]
    [InlineData("PP-999", "PP-1000")]
    public void Suggest_IncrementsLongestRightmostDigitRun(string article, string expected) =>
        MaterialArticleSuggester.Suggest(article, Array.Empty<string>()).Should().Be(expected);

    [Fact]
    public void Suggest_EmptyDictionary_ReturnsNull() =>
        MaterialArticleSuggester.Suggest(null, Array.Empty<string>()).Should().BeNull();

    [Fact]
    public void Suggest_SkipsExistingArticles_CaseInsensitive() =>
        MaterialArticleSuggester.Suggest("PP-0099", new[] { "pp-0100", "PP-0101" })
            .Should().Be("PP-0102");

    [Fact]
    public void Suggest_WithoutDigits_SkipsExistingArticlesAfterAppendingZero() =>
        MaterialArticleSuggester.Suggest("ABC", new[] { "ABC0", "abc1" })
            .Should().Be("ABC2");

    [Fact]
    public void Suggest_ResultLongerThanArticleLimit_ReturnsNull() =>
        MaterialArticleSuggester.Suggest(new string('A', 49) + "9", Array.Empty<string>())
            .Should().BeNull();
}
