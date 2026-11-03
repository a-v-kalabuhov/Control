using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class ProductRecipeTests
{
    private static readonly ISet<Guid> NoExisting = new HashSet<Guid>();

    private static RecipeRow Plastic(Guid id, decimal percent, bool isActive = true) =>
        new(id, MaterialType.Plastic, isActive, percent);

    [Fact]
    public void SetRecipe_AddsRowsInGivenOrder()
    {
        var product = new Product();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        product.SetRecipe(new[] { Plastic(a, 60), Plastic(b, 40) }, NoExisting);

        product.RecipeIngredients.Should().HaveCount(2);
        product.RecipeIngredients.Single(i => i.MaterialId == a).SortOrder.Should().Be(0);
        product.RecipeIngredients.Single(i => i.MaterialId == b).SortOrder.Should().Be(1);
        product.RecipeIngredients.Single(i => i.MaterialId == a).Percent.Should().Be(60m);
    }

    [Fact]
    public void SetRecipe_ReplacesWholeSet()
    {
        var product = new Product();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        product.SetRecipe(new[] { Plastic(a, 50), Plastic(b, 50) }, NoExisting);

        product.SetRecipe(new[] { Plastic(b, 30), Plastic(c, 70) }, NoExisting);

        product.RecipeIngredients.Select(i => i.MaterialId).Should().BeEquivalentTo(new[] { b, c });
        product.RecipeIngredients.Single(i => i.MaterialId == b).SortOrder.Should().Be(0);
        product.RecipeIngredients.Single(i => i.MaterialId == c).Percent.Should().Be(70m);
    }

    [Fact]
    public void SetRecipe_KeepsExistingRowInstance()
    {
        var product = new Product();
        var a = Guid.NewGuid();
        product.SetRecipe(new[] { Plastic(a, 50) }, NoExisting);
        var row = product.RecipeIngredients.Single();

        product.SetRecipe(new[] { Plastic(a, 90) }, NoExisting);

        product.RecipeIngredients.Should().ContainSingle().Which.Should().BeSameAs(row);
        row.Percent.Should().Be(90m);
    }

    [Fact]
    public void SetRecipe_Empty_ClearsRecipe()
    {
        var product = new Product();
        product.SetRecipe(new[] { Plastic(Guid.NewGuid(), 100) }, NoExisting);

        product.SetRecipe(Array.Empty<RecipeRow>(), NoExisting);

        product.RecipeIngredients.Should().BeEmpty();
    }

    [Fact]
    public void SetRecipe_InvalidSum_DoesNotThrow()
    {
        var product = new Product();

        FluentActions.Invoking(() => product.SetRecipe(new[] { Plastic(Guid.NewGuid(), 98) }, NoExisting))
            .Should().NotThrow();
    }

    [Fact]
    public void SetRecipe_RuleViolation_ThrowsAndKeepsOldRecipe()
    {
        var product = new Product();
        var a = Guid.NewGuid();
        product.SetRecipe(new[] { Plastic(a, 100) }, NoExisting);

        FluentActions.Invoking(() => product.SetRecipe(new[] { Plastic(Guid.NewGuid(), 0) }, NoExisting))
            .Should().Throw<DomainException>();
        product.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(a);
    }

    [Fact]
    public void SetRecipe_NewArchivedMaterial_Throws()
    {
        var product = new Product();

        FluentActions.Invoking(() => product.SetRecipe(new[] { Plastic(Guid.NewGuid(), 100, isActive: false) }, NoExisting))
            .Should().Throw<DomainException>();
    }
}
