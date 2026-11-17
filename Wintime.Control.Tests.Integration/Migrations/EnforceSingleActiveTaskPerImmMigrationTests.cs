using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

public class EnforceSingleActiveTaskPerImmMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_WithExistingExecutingDuplicates_ExplainsRequiredManualResolution()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_EnforceSingleActiveTaskPerImm"));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1]);

        var immId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var moldId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_pg.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES (gen_random_uuid(),'t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
SELECT '{immId}',now(),TRUE,'ТПА-1',""Id"" FROM ""Templates"" LIMIT 1;
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{typeId}','ACTIVE-TASK',now(),TRUE,'Тип');
INSERT INTO ""Products"" (""Id"",""Article"",""CreatedAt"",""FullArticle"",""IsActive"",""Name"",""ProductTypeId"")
VALUES ('{productId}','0',now(),'ACTIVE-TASK.0',TRUE,'Деталь','{typeId}');
INSERT INTO ""Molds"" (""Id"",""Cavities"",""CreatedAt"",""FormId"",""IsActive"",""MaxResourceCycles"",""Name"",""RunnerWeightGrams"",""ProductTypeId"")
VALUES ('{moldId}',1,now(),'ACTIVE-1',TRUE,100000,'Форма',0,'{typeId}');
INSERT INTO ""ShiftTasks"" (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""Cavities"",""CreatedAt"",""DefectQuantity"",""ImmId"",""MoldId"",""ProductId"",""PlanQuantity"",""PlannedDate"",""PlannedSetupMinutes"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
VALUES
(gen_random_uuid(),0,0,1,now(),0,'{immId}','{moldId}','{productId}',100,current_date,0,0,0,5,0),
(gen_random_uuid(),0,0,1,now(),0,'{immId}','{moldId}','{productId}',100,current_date,0,0,0,2,0);", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var act = async () => await migrator.MigrateAsync(target);

        var assertion = await act.Should().ThrowAsync<PostgresException>();
        assertion.Which.Message.Should().Contain("EnforceSingleActiveTaskPerImm");
        assertion.Which.Message.Should().Contain("Setup/InProgress");
        assertion.Which.Message.Should().Contain(immId.ToString());
    }
}
