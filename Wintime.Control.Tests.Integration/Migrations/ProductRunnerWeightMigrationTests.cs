using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>Перенос веса литника из ПФ в изделия с сохранением существующего значения.</summary>
public class ProductRunnerWeightMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_CopiesEarliestMoldRunnerWeightToProducts_RecalculatesTasks_ThenDropsMoldColumn()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_ProductRunnerWeight"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        var typeId = Guid.NewGuid();
        var product0Id = Guid.NewGuid();
        var product1Id = Guid.NewGuid();
        var mold1Id = Guid.NewGuid();
        var mold2Id = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{typeId}','RUNNER',now(),TRUE,'Литниковый тип');
INSERT INTO ""Products"" (""Id"",""Article"",""CreatedAt"",""FullArticle"",""IsActive"",""Name"",""ProductTypeId"")
VALUES ('{product0Id}','0',now(),'RUNNER.0',TRUE,'По умолчанию','{typeId}'),
       ('{product1Id}','BLK',now(),'RUNNER.BLK',TRUE,'Чёрный','{typeId}');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{mold1Id}',2,now() - interval '1 day','RUNNER-1',TRUE,100000,'Форма 1',2,'{typeId}'),
       ('{mold2Id}',1,now(),'RUNNER-2',TRUE,100000,'Форма 2',3,'{typeId}');
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES (gen_random_uuid(),'t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
SELECT gen_random_uuid(),now(),TRUE,'ТПА-1',""Id"" FROM ""Templates"" LIMIT 1;
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""ProductId"",""PartWeightGrams"",""Cavities"",""PlannedFullCycleSeconds"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
SELECT '{taskId}',23,2,now(),0,""Id"",'{mold2Id}','{product0Id}',10,2,60,100,current_date,0,0,0,3,0 FROM ""Imms"" LIMIT 1;
INSERT INTO ""ImmCycles"" (""Id"",""CreatedAt"",""ImmId"",""MoldId"",""TaskId"",""StartTime"",""EndTime"",""DurationSeconds"",""Cavities"",""IsSuccessful"")
SELECT '{cycleId}',now(),""Id"",'{mold2Id}','{taskId}',now() - interval '1 minute',now(),60,2,TRUE FROM ""Imms"" LIMIT 1;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(target);

        await using var verifyConn = new NpgsqlConnection(_pg.GetConnectionString());
        await verifyConn.OpenAsync();
        var productWeights = new Dictionary<string, decimal>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT \"Article\", \"RunnerWeightGrams\" FROM \"Products\"", verifyConn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                productWeights.Add(reader.GetString(0), reader.GetDecimal(1));
        }
        productWeights.Values.Should().OnlyContain(weight => weight == 2m);

        await using (var cmd = new NpgsqlCommand(
            $"SELECT \"ActualMaterialWeightGrams\" FROM \"ShiftTasks\" WHERE \"Id\" = '{taskId}'", verifyConn))
            (await cmd.ExecuteScalarAsync()).Should().Be(22m); // 2 детали × 10 г + литник нового изделия 2 г

        await using var verifyCmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_name = 'Molds' AND column_name = 'RunnerWeightGrams'", verifyConn);
        (await verifyCmd.ExecuteScalarAsync()).Should().Be(0L);
    }
}
