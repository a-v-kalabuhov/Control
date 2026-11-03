using FluentAssertions;
using Wintime.Control.Core.Entities;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

/// <summary>Сущности строк рецепта (BL-36): навигации есть и не null у новых объектов.</summary>
public class RecipeEntitiesTests
{
    [Fact]
    public void Product_HasEmptyRecipeByDefault()
    {
        var product = new Product();

        product.RecipeIngredients.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ShiftTask_HasEmptyRecipeByDefault()
    {
        var task = new ShiftTask();

        task.RecipeIngredients.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ProductRecipeIngredient_CarriesMaterialAndOrder()
    {
        var row = new ProductRecipeIngredient
        {
            ProductId = Guid.NewGuid(), MaterialId = Guid.NewGuid(), Percent = 60.5m, SortOrder = 1
        };

        row.Percent.Should().Be(60.5m);
        row.SortOrder.Should().Be(1);
        row.Id.Should().NotBe(Guid.Empty);
    }
}
