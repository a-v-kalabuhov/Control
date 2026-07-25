using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductTypeEntityTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ProductTypeEntityTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ProductType_PersistsAndLinksToMold()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var pt = new ProductType { Article = $"ART-{Guid.NewGuid():N}", Name = "Изделие" };
        db.ProductTypes.Add(pt);
        var mold = new Mold
        {
            Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 2,
            IsActive = true, ProductTypeId = pt.Id
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        var loaded = await db.Molds.Include(m => m.ProductType).FirstAsync(m => m.Id == mold.Id);
        loaded.ProductType.Should().NotBeNull();
        loaded.ProductType!.Article.Should().Be(pt.Article);
    }

    [Fact]
    public async Task ProductType_DuplicateArticle_ThrowsOnUniqueIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var article = $"DUP-{Guid.NewGuid():N}";
        db.ProductTypes.Add(new ProductType { Article = article, Name = "A" });
        await db.SaveChangesAsync();

        db.ProductTypes.Add(new ProductType { Article = article, Name = "B" });
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
