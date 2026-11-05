using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>PZP-24: перенос весов с непустой БД до удаления колонки ПФ.</summary>
public class Pzp24ProductPartWeightMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_CopiesMoldWeightsToProductsAndTasks_ThenDropsMoldColumn()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_Pzp24ProductPartWeight"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        var typeId = Guid.NewGuid();
        var product0Id = Guid.NewGuid();
        var product1Id = Guid.NewGuid();
        var mold1Id = Guid.NewGuid();
        var mold2Id = Guid.NewGuid();
        var task1Id = Guid.NewGuid();
        var task2Id = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES (gen_random_uuid(),'t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
SELECT gen_random_uuid(),now(),TRUE,'ТПА-1',""Id"" FROM ""Templates"" LIMIT 1;
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{typeId}','WEIGHT',now(),TRUE,'Весовой тип');
INSERT INTO ""Products"" (""Id"",""Article"",""CreatedAt"",""FullArticle"",""IsActive"",""Name"",""ProductTypeId"")
VALUES ('{product0Id}','0',now(),'WEIGHT.0',TRUE,'По умолчанию','{typeId}'),
       ('{product1Id}','BLK',now(),'WEIGHT.BLK',TRUE,'Чёрный','{typeId}');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""PartWeightGrams"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{mold1Id}',2,now() - interval '1 day','WF-1',TRUE,100000,'Форма 1',10,2,'{typeId}'),
       ('{mold2Id}',1,now(),'WF-2',TRUE,100000,'Форма 2',12,3,'{typeId}');
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""ProductId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
SELECT '{task1Id}',0,0,now(),0,""Id"",'{mold1Id}','{product0Id}',100,current_date,0,0,0,0,0 FROM ""Imms"" LIMIT 1;
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""ProductId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
SELECT '{task2Id}',0,0,now(),0,""Id"",'{mold2Id}','{product1Id}',100,current_date,0,0,0,0,0 FROM ""Imms"" LIMIT 1;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(target);

        await using var check = NewContext();
        (await check.Products.OrderBy(p => p.Article).ToListAsync())
            .Should().OnlyContain(p => p.PartWeightGrams == 10m);
        (await check.ShiftTasks.SingleAsync(t => t.Id == task1Id)).PartWeightGrams.Should().Be(10m);
        (await check.ShiftTasks.SingleAsync(t => t.Id == task2Id)).PartWeightGrams.Should().Be(12m);
        await using var verifyConn = new NpgsqlConnection(_pg.GetConnectionString());
        await verifyConn.OpenAsync();
        await using var verifyCmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Molds' AND column_name = 'PartWeightGrams'", verifyConn);
        (await verifyCmd.ExecuteScalarAsync()).Should().Be(0L);
    }
}
