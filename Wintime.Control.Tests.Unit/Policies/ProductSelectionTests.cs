using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ProductSelectionTests
{
    private static Product MakeProduct(bool productActive = true, bool typeActive = true)
    {
        var type = new ProductType { Article = "A1", Name = "Тип", IsActive = typeActive };
        return new Product { ProductTypeId = type.Id, ProductType = type, Article = "0",
                             Name = "По умолчанию", FullArticle = "A1.0", IsActive = productActive };
    }

    [Fact]
    public void EnsureSelectable_Active_DoesNotThrow() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct())).Should().NotThrow();

    [Fact]
    public void EnsureSelectable_ArchivedProduct_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct(productActive: false)))
            .Should().Throw<DomainException>().WithMessage("Изделие в архиве");

    [Fact]
    public void EnsureSelectable_ArchivedType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(MakeProduct(typeActive: false)))
            .Should().Throw<DomainException>().WithMessage("Тип изделия в архиве");

    [Fact]
    public void EnsureSelectable_TypeNotLoaded_ThrowsInvalidOperation()
    {
        var p = MakeProduct();
        p.ProductType = null;
        FluentActions.Invoking(() => ProductSelection.EnsureSelectable(p)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureBelongsToMold_SameType_DoesNotThrow()
    {
        var p = MakeProduct();
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(p, p.ProductTypeId)).Should().NotThrow();
    }

    [Fact]
    public void EnsureBelongsToMold_OtherType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(MakeProduct(), Guid.NewGuid()))
            .Should().Throw<DomainException>().WithMessage("Изделие не относится к типу изделия пресс-формы");

    [Fact]
    public void EnsureBelongsToMold_MoldWithoutType_Throws() =>
        FluentActions.Invoking(() => ProductSelection.EnsureBelongsToMold(MakeProduct(), null))
            .Should().Throw<DomainException>().WithMessage("У пресс-формы не задан тип изделия");
}
