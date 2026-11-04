using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskRecipeTests
{
    private static Material Plastic(string article = "PP-01") =>
        new() { Type = MaterialType.Plastic, Article = article, Name = "Полипропилен " + article };

    private static ProductRecipeIngredient Row(Material material, decimal percent, int order) =>
        new() { MaterialId = material.Id, Material = material, Percent = percent, SortOrder = order };

    [Fact]
    public void ApplyRecipe_FillsCopyWithOriginalEqualMaterial()
    {
        var a = Plastic("A");
        var b = Plastic("B");
        var task = new ShiftTask();

        task.ApplyRecipe(new[] { Row(a, 60m, 0), Row(b, 40m, 1) });

        task.RecipeIngredients.Should().HaveCount(2);
        task.RecipeIngredients.Should().OnlyContain(i => i.MaterialId == i.OriginalMaterialId);
        task.RecipeIngredients.Single(i => i.MaterialId == a.Id).Percent.Should().Be(60m);
        task.RecipeIngredients.Single(i => i.MaterialId == b.Id).SortOrder.Should().Be(1);
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void ApplyRecipe_NumbersOrderByProductRecipeOrder()
    {
        var a = Plastic("A");
        var b = Plastic("B");
        var task = new ShiftTask();

        // Порядок в списке не совпадает с SortOrder — копия идёт по SortOrder.
        task.ApplyRecipe(new[] { Row(b, 40m, 1), Row(a, 60m, 0) });

        task.RecipeIngredients.OrderBy(i => i.SortOrder).Select(i => i.MaterialId).Should().Equal(a.Id, b.Id);
        task.RecipeIngredients.Select(i => i.SortOrder).Should().BeEquivalentTo(new[] { 0, 1 });
    }

    [Fact]
    public void ApplyRecipe_OverwritesPreviousCopy()
    {
        var a = Plastic("A");
        var b = Plastic("B");
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(a, 100m, 0) });

        task.ApplyRecipe(new[] { Row(b, 100m, 0) });

        task.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(b.Id);
    }

    [Fact]
    public void ApplyRecipe_ReusesRowsByCurrentMaterial_AndResetsManualEdit()
    {
        var original = Plastic("ORIGINAL");
        var removed = Plastic("REMOVED");
        var shared = Plastic("SHARED");
        var added = Plastic("ADDED");
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(original, 60m, 0), Row(removed, 40m, 1) });
        var reused = task.RecipeIngredients.Single(i => i.MaterialId == original.Id);
        task.ReplaceIngredient(reused.Id, shared);
        task.RecipeEdited.Should().BeTrue();

        task.ApplyRecipe(new[] { Row(added, 25m, 0), Row(shared, 75m, 1) });

        task.RecipeIngredients.Select(i => i.MaterialId).Should().BeEquivalentTo(new[] { added.Id, shared.Id });
        task.RecipeIngredients.Single(i => i.MaterialId == shared.Id).Should().BeSameAs(reused);
        reused.OriginalMaterialId.Should().Be(shared.Id);
        reused.OriginalMaterial.Should().BeSameAs(shared);
        reused.Material.Should().BeSameAs(shared);
        reused.Percent.Should().Be(75m);
        reused.SortOrder.Should().Be(1);
        task.RecipeIngredients.OrderBy(i => i.SortOrder).Select(i => i.MaterialId).Should().Equal(added.Id, shared.Id);
        task.RecipeIngredients.Select(i => i.SortOrder).Should().BeEquivalentTo(new[] { 0, 1 });
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void ApplyRecipe_Empty_ClearsCopy()
    {
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(Plastic(), 100m, 0) });

        task.ApplyRecipe(Array.Empty<ProductRecipeIngredient>());

        task.RecipeIngredients.Should().BeEmpty();
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void SetProduct_SameProduct_ReturnsFalse_AndKeepsCopy()
    {
        var a = Plastic("A");
        var productId = Guid.NewGuid();
        var task = new ShiftTask { Status = TaskStatus.Planned, ProductId = productId };
        task.ApplyRecipe(new[] { Row(a, 100m, 0) });

        task.SetProduct(productId).Should().BeFalse();
        task.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(a.Id);
    }

    [Fact]
    public void SetProduct_NewProduct_ReturnsTrue()
    {
        var task = new ShiftTask { Status = TaskStatus.Planned, ProductId = Guid.NewGuid() };
        var other = Guid.NewGuid();

        task.SetProduct(other).Should().BeTrue();
        task.ProductId.Should().Be(other);
    }

    [Fact]
    public void ReplaceIngredient_DifferentType_Throws()
    {
        var plastic = Plastic();
        var colorant = new Material { Type = MaterialType.Colorant, Article = "C-1", Name = "Краситель", ColorName = "Чёрный" };
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(plastic, 100m, 0) });
        var row = task.RecipeIngredients.Single();

        FluentActions.Invoking(() => task.ReplaceIngredient(row.Id, colorant))
            .Should().Throw<DomainException>().WithMessage("*пластик*");
        row.MaterialId.Should().Be(plastic.Id);
    }

    [Fact]
    public void ReplaceIngredient_ArchivedMaterial_Throws()
    {
        var plastic = Plastic();
        var archived = new Material { Type = MaterialType.Plastic, Article = "OLD", Name = "Старый", IsActive = false };
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(plastic, 100m, 0) });
        var row = task.RecipeIngredients.Single();

        FluentActions.Invoking(() => task.ReplaceIngredient(row.Id, archived))
            .Should().Throw<DomainException>().WithMessage("*рхив*");
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    [InlineData(TaskStatus.InProgress)]
    public void ReplaceIngredient_AllowedInAnyStatus(TaskStatus status)
    {
        var plastic = Plastic();
        var other = Plastic("NEW");
        var task = new ShiftTask { Status = status };
        task.ApplyRecipe(new[] { Row(plastic, 100m, 0) });
        var row = task.RecipeIngredients.Single();

        FluentActions.Invoking(() => task.ReplaceIngredient(row.Id, other)).Should().NotThrow();
        row.MaterialId.Should().Be(other.Id);
        task.RecipeEdited.Should().BeTrue();
    }

    [Fact]
    public void ReplaceIngredient_MaterialAlreadyUsedByAnotherRow_ThrowsAndKeepsCopyUnchanged()
    {
        var a = Plastic("A");
        var b = Plastic("B");
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(a, 60m, 0), Row(b, 40m, 1) });
        var first = task.RecipeIngredients.Single(i => i.MaterialId == a.Id);
        var second = task.RecipeIngredients.Single(i => i.MaterialId == b.Id);

        FluentActions.Invoking(() => task.ReplaceIngredient(first.Id, b))
            .Should().Throw<DomainException>().WithMessage("*уже используется*");

        first.MaterialId.Should().Be(a.Id);
        first.Material.Should().BeSameAs(a);
        second.MaterialId.Should().Be(b.Id);
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void ReplaceIngredient_SameMaterial_IsNoOp()
    {
        var plastic = Plastic();
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(plastic, 100m, 0) });
        var row = task.RecipeIngredients.Single();

        FluentActions.Invoking(() => task.ReplaceIngredient(row.Id, plastic)).Should().NotThrow();

        row.MaterialId.Should().Be(plastic.Id);
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void ReplaceIngredient_BackToOriginal_ResetsRecipeEdited()
    {
        var plastic = Plastic();
        var other = Plastic("NEW");
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(plastic, 100m, 0) });
        var row = task.RecipeIngredients.Single();
        task.ReplaceIngredient(row.Id, other);
        task.RecipeEdited.Should().BeTrue();

        task.ReplaceIngredient(row.Id, plastic);

        task.RecipeEdited.Should().BeFalse();
        row.MaterialId.Should().Be(plastic.Id);
    }

    [Fact]
    public void ReplaceIngredient_UnknownRow_Throws()
    {
        var task = new ShiftTask();
        task.ApplyRecipe(new[] { Row(Plastic(), 100m, 0) });

        FluentActions.Invoking(() => task.ReplaceIngredient(Guid.NewGuid(), Plastic("NEW")))
            .Should().Throw<DomainException>();
    }
}
