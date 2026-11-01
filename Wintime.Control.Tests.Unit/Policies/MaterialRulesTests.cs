using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class MaterialRulesTests
{
    private static MaterialFields Fields(string article = "PP-01", string name = "Полипропилен",
        string? manufacturer = null, string? colorName = null, string? colorCode = null, string? comment = null) =>
        MaterialRules.Normalize(article, name, manufacturer, colorName, colorCode, comment);

    [Fact]
    public void Normalize_UppercasesArticleAndTrimsAll()
    {
        var f = MaterialRules.Normalize("  pp-01 ", "  Полипропилен ", " Сибур ", " Чёрный ", " ral 9005 ", " прим ");

        f.Article.Should().Be("PP-01");
        f.Name.Should().Be("Полипропилен");
        f.Manufacturer.Should().Be("Сибур");
        f.ColorName.Should().Be("Чёрный");
        f.ColorCode.Should().Be("ral 9005");   // регистр кода цвета не трогаем
        f.Comment.Should().Be("прим");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_EmptyOptionalBecomesNull(string? value)
    {
        var f = MaterialRules.Normalize("A", "B", value, value, value, value);

        f.Manufacturer.Should().BeNull();
        f.ColorName.Should().BeNull();
        f.ColorCode.Should().BeNull();
        f.Comment.Should().BeNull();
    }

    [Fact]
    public void Normalize_NullRequiredBecomesEmpty()
    {
        var f = MaterialRules.Normalize(null, null, null, null, null, null);
        f.Article.Should().BeEmpty();
        f.Name.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Plastic_WithoutColor_IsValid() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields()).Should().BeNull();

    [Fact]
    public void Validate_Colorant_WithoutColor_Fails() =>
        MaterialRules.Validate(MaterialType.Colorant, Fields()).Should().Be("Для красителя укажите цвет.");

    [Fact]
    public void Validate_Colorant_WithColor_IsValid() =>
        MaterialRules.Validate(MaterialType.Colorant, Fields(colorName: "Чёрный")).Should().BeNull();

    [Fact]
    public void Validate_EmptyArticle_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(article: "  ")).Should().Be("Артикул обязателен.");

    [Fact]
    public void Validate_EmptyName_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(name: "")).Should().Be("Наименование обязательно.");

    [Fact]
    public void Validate_TooLongArticle_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(article: new string('A', MaterialRules.ArticleMaxLength + 1)))
            .Should().Be($"Артикул длиннее {MaterialRules.ArticleMaxLength} символов.");

    [Fact]
    public void Validate_TooLongComment_Fails() =>
        MaterialRules.Validate(MaterialType.Plastic, Fields(comment: new string('x', MaterialRules.CommentMaxLength + 1)))
            .Should().Be($"Комментарий длиннее {MaterialRules.CommentMaxLength} символов.");

    [Theory]
    [InlineData(MaterialType.Plastic, "пластик")]
    [InlineData(MaterialType.Colorant, "краситель")]
    public void TypeLabel_IsRussian(MaterialType type, string expected) =>
        MaterialRules.TypeLabel(type).Should().Be(expected);
}
