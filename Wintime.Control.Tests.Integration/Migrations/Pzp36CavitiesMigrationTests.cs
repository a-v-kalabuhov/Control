using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>PZP-36: одноразовый перенос максимума ПФ в рабочую гнёздовость существующего задания.</summary>
public class Pzp36CavitiesMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_BackfillsTaskFromMold_AndPreservesCycleSnapshot()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_Pzp36Cavities"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        var typeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var moldId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES (gen_random_uuid(),'t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
SELECT gen_random_uuid(),now(),TRUE,'ТПА-1',""Id"" FROM ""Templates"" LIMIT 1;
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{typeId}','CAVITIES',now(),TRUE,'Гнёздовой тип');
INSERT INTO ""Products"" (""Id"",""Article"",""CreatedAt"",""FullArticle"",""IsActive"",""Name"",""ProductTypeId"")
VALUES ('{productId}','0',now(),'CAVITIES.0',TRUE,'Деталь','{typeId}');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{moldId}',4,now(),'CAV-1',TRUE,100000,'Форма',0,'{typeId}');
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""ProductId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
SELECT '{taskId}',0,0,now(),0,""Id"",'{moldId}','{productId}',100,current_date,0,0,0,0,0 FROM ""Imms"" LIMIT 1;
INSERT INTO ""ImmCycles"" (""Id"",""CreatedAt"",""ImmId"",""MoldId"",""TaskId"",""StartTime"",""DurationSeconds"",""Cavities"",""IsSuccessful"")
SELECT '{cycleId}',now(),""Id"",'{moldId}','{taskId}',now(),60,1,TRUE FROM ""Imms"" LIMIT 1;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(target);

        await using var verify = new NpgsqlConnection(_pg.GetConnectionString());
        await verify.OpenAsync();
        await using var taskCmd = new NpgsqlCommand("SELECT \"Cavities\" FROM \"ShiftTasks\" WHERE \"Id\" = @id", verify);
        taskCmd.Parameters.AddWithValue("id", taskId);
        (await taskCmd.ExecuteScalarAsync()).Should().Be(4);
        await using var cycleCmd = new NpgsqlCommand("SELECT \"Cavities\" FROM \"ImmCycles\" WHERE \"Id\" = @id", verify);
        cycleCmd.Parameters.AddWithValue("id", cycleId);
        (await cycleCmd.ExecuteScalarAsync()).Should().Be(1);
        await using var productCmd = new NpgsqlCommand("SELECT \"Cavities\" FROM \"Products\" WHERE \"Id\" = @id", verify);
        productCmd.Parameters.AddWithValue("id", productId);
        (await productCmd.ExecuteScalarAsync()).Should().Be(DBNull.Value);
    }
}
