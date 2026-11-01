using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Materials;

[Collection("Integration")]
public class MaterialEntityTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MaterialEntityTests(IntegrationTestFactory factory) => _factory = factory;

    private ControlDbContext NewDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    private static string Uniq(string prefix) => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();

    [Fact]
    public async Task Material_Persists()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var m = new Material { Type = MaterialType.Colorant, Article = Uniq("C"), Name = "Краситель", ColorName = "Чёрный" };
        db.Materials.Add(m);
        await db.SaveChangesAsync();

        var loaded = await db.Materials.AsNoTracking().FirstAsync(x => x.Id == m.Id);
        loaded.Type.Should().Be(MaterialType.Colorant);
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SameTypeAndArticle_ViolatesUniqueIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var article = Uniq("P");
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("A") });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("B") });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SameArticle_DifferentType_IsAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var article = Uniq("X");
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = article, Name = Uniq("A") });
        db.Materials.Add(new Material { Type = MaterialType.Colorant, Article = article, Name = Uniq("B"), ColorName = "Белый" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ManufacturerAndName_CaseInsensitive_ViolatesExpressionIndex()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var name = $"Полипропилен {Guid.NewGuid():N}";
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name, Manufacturer = "Сибур" });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name.ToUpperInvariant(), Manufacturer = "СИБУР" });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task NullManufacturer_IsOneValue_ForUniqueness()
    {
        using var scope = _factory.Services.CreateScope();
        var db = NewDb(scope);
        var name = $"Без производителя {Guid.NewGuid():N}";
        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name });
        await db.SaveChangesAsync();

        db.Materials.Add(new Material { Type = MaterialType.Plastic, Article = Uniq("P"), Name = name });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
