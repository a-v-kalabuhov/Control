using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>
/// Перенос данных миграции AddProducts на непустой БД: обычные тесты гоняют миграции на пустой.
/// Отдельный контейнер на тест — миграции до предыдущей, сид сырым SQL, затем AddProducts.
/// </summary>
public class AddProductsMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    private static readonly Guid TemplateId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ImmId      = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TypeId     = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid MoldId     = Guid.Parse("00000000-0000-0000-0000-000000000004");

    // Сид в схеме ДО AddProducts. moldTypeSql — значение "ProductTypeId" ПФ ('...' или NULL).
    private static string SeedSql(string moldTypeSql) => $@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES ('{TemplateId}','t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
VALUES ('{ImmId}',now(),TRUE,'ТПА-1','{TemplateId}');
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{TypeId}','abc ',now(),TRUE,'Крышка');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""PartWeightGrams"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{MoldId}',2,now(),'F-1',TRUE,100000,'Форма',10,2,{moldTypeSql});
INSERT INTO ""Orders"" (""Id"",""CreatedAt"",""DueDate"",""Number"",""ProductTypeId"",""Quantity"",""Status"")
VALUES (gen_random_uuid(),now(),now() + interval '7 days','ORD-1','{TypeId}',100,0);
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
VALUES (gen_random_uuid(),0,0,now(),0,'{ImmId}','{MoldId}',100,current_date,0,0,0,0,0);";

    private async Task<(IMigrator Migrator, string Target)> MigrateToPreviousAndSeedAsync(ControlDbContext db, string moldTypeSql)
    {
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_AddProducts"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(SeedSql(moldTypeSql), conn);
        await cmd.ExecuteNonQueryAsync();
        return (migrator, target);
    }

    [Fact]
    public async Task AddProducts_MigratesExistingTypesOrdersAndTasks()
    {
        await using var db = NewContext();
        var (migrator, target) = await MigrateToPreviousAndSeedAsync(db, $"'{TypeId}'");

        await migrator.MigrateAsync(target);

        await using var check = NewContext();
        (await check.ProductTypes.SingleAsync()).Article.Should().Be("ABC");   // 'abc ' нормализован
        var product = await check.Products.SingleAsync();
        product.FullArticle.Should().Be("ABC.0");
        product.Article.Should().Be("0");
        product.Name.Should().Be("По умолчанию");
        (await check.ShiftTasks.SingleAsync()).ProductId.Should().Be(product.Id);
        (await check.Orders.SingleAsync()).ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task AddProducts_TaskOnMoldWithoutType_FailsWithMessage()
    {
        await using var db = NewContext();
        var (migrator, target) = await MigrateToPreviousAndSeedAsync(db, "NULL");

        await FluentActions.Awaiting(() => migrator.MigrateAsync(target))
            .Should().ThrowAsync<PostgresException>()
            .Where(e => e.MessageText.Contains("PZP-19"));
    }
}
