using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class RecipeRulesTests
{
    private static RecipeRow Plastic(decimal percent, Guid? id = null, bool isActive = true) =>
        new(id ?? Guid.NewGuid(), MaterialType.Plastic, isActive, percent);

    private static RecipeRow Colorant(decimal percent, Guid? id = null, bool isActive = true) =>
        new(id ?? Guid.NewGuid(), MaterialType.Colorant, isActive, percent);

    private static readonly ISet<Guid> NoExisting = new HashSet<Guid>();

    [Fact]
    public void Validate_EmptyRecipe_IsAllowed()
    {
        FluentActions.Invoking(() => RecipeRules.Validate(Array.Empty<RecipeRow>(), NoExisting))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_EightRows_IsAllowed()
    {
        var rows = Enumerable.Range(0, RecipeRules.MaxIngredients).Select(_ => Plastic(12.5m)).ToList();

        FluentActions.Invoking(() => RecipeRules.Validate(rows, NoExisting)).Should().NotThrow();
    }

    [Fact]
    public void Validate_NineRows_Throws()
    {
        var rows = Enumerable.Range(0, RecipeRules.MaxIngredients + 1).Select(_ => Plastic(11m)).ToList();

        FluentActions.Invoking(() => RecipeRules.Validate(rows, NoExisting))
            .Should().Throw<DomainException>().WithMessage("*8*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Validate_PercentOutOfRange_Throws(decimal percent)
    {
        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Plastic(percent) }, NoExisting))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_ThreeDecimalPlaces_Throws()
    {
        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Plastic(33.333m) }, NoExisting))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_DuplicateMaterial_Throws()
    {
        var id = Guid.NewGuid();

        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Plastic(50, id), Plastic(50, id) }, NoExisting))
            .Should().Throw<DomainException>().WithMessage("*дважды*");
    }

    [Fact]
    public void Validate_NonEmptyWithoutPlastic_Throws()
    {
        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Colorant(2) }, NoExisting))
            .Should().Throw<DomainException>().WithMessage("*пластик*");
    }

    [Fact]
    public void Validate_NewArchivedMaterial_Throws()
    {
        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Plastic(100, isActive: false) }, NoExisting))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Validate_ExistingArchivedMaterial_IsAllowed()
    {
        var id = Guid.NewGuid();
        var existing = new HashSet<Guid> { id };

        FluentActions.Invoking(() => RecipeRules.Validate(new[] { Plastic(100, id, isActive: false) }, existing))
            .Should().NotThrow();
    }

    [Fact]
    public void PlasticSum_IgnoresColorant()
    {
        RecipeRules.PlasticSum(new[] { Plastic(60), Plastic(38), Colorant(2) }).Should().Be(98m);
    }

    [Fact]
    public void PlasticSum_Empty_IsZero()
    {
        RecipeRules.PlasticSum(Array.Empty<RecipeRow>()).Should().Be(0m);
    }

    [Fact]
    public void IsSumOk_Empty_IsTrue()
    {
        RecipeRules.IsSumOk(Array.Empty<RecipeRow>()).Should().BeTrue();
    }

    [Fact]
    public void IsSumOk_PlasticSum100_IsTrue()
    {
        RecipeRules.IsSumOk(new[] { Plastic(60), Plastic(40), Colorant(2) }).Should().BeTrue();
    }

    [Fact]
    public void IsSumOk_PlasticSum98_IsFalse()
    {
        RecipeRules.IsSumOk(new[] { Plastic(98), Colorant(2) }).Should().BeFalse();
    }
}
