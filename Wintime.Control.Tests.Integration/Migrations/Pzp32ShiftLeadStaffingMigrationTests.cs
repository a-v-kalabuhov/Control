using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>PZP-32: схема плана смен и ограничения активных назначений литейщиков.</summary>
public class Pzp32ShiftLeadStaffingMigrationTests : IAsyncLifetime
{
    private static readonly Guid TemplateId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Imm1Id = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Imm2Id = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ShiftId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid DayId = Guid.Parse("10000000-0000-0000-0000-000000000010");
    private static readonly Guid Item1Id = Guid.Parse("10000000-0000-0000-0000-000000000011");
    private static readonly Guid Item2Id = Guid.Parse("10000000-0000-0000-0000-000000000012");

    private const string Lead1Id = "pzp32-lead-1";
    private const string Lead2Id = "pzp32-lead-2";
    private const string Operator1Id = "pzp32-operator-1";
    private const string Operator2Id = "pzp32-operator-2";
    private const string Operator3Id = "pzp32-operator-3";
    private const string Operator4Id = "pzp32-operator-4";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_CreatesTablesUniqueIndexesAndNoShiftForeignKey()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);

        (await ScalarAsync<long>(@"
SELECT count(*) FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_name IN ('ShiftPlanDays', 'ShiftPlanItems', 'ShiftImmOperatorAssignments')"))
            .Should().Be(3);

        await InsertDayAndItemAsync(DayId, Item1Id);
        await ExpectSqlStateAsync($@"
INSERT INTO ""ShiftPlanDays"" (""Id"",""CreatedAt"",""ShiftDate"")
VALUES ('{Guid.NewGuid()}',now(),DATE '2026-11-08')", "23505");
        await ExpectSqlStateAsync(ItemSql(Item2Id, 1), "23505");

        (await ScalarAsync<long>(@"
SELECT count(*) FROM pg_constraint
WHERE conrelid = '""ShiftPlanItems""'::regclass
  AND confrelid = '""Shifts""'::regclass
  AND contype = 'f'")).Should().Be(0);
    }

    [Fact]
    public async Task ActiveSlot_IsUniqueAndCanBeReusedAfterUnassignment()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);
        await InsertDayAndItemAsync(DayId, Item1Id);
        var firstAssignmentId = Guid.NewGuid();
        await ExecuteSqlAsync(AssignmentSql(firstAssignmentId, Item1Id, Imm1Id, Operator1Id, 1));

        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator2Id, 1), "23505");

        await ExecuteSqlAsync($@"
UPDATE ""ShiftImmOperatorAssignments""
SET ""UnassignedAt"" = ""AssignedAt""
WHERE ""Id"" = '{firstAssignmentId}'");
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator2Id, 1));

        (await ScalarAsync<long>($@"
SELECT count(*) FROM ""ShiftImmOperatorAssignments""
WHERE ""ShiftPlanItemId"" = '{Item1Id}' AND ""ImmId"" = '{Imm1Id}' AND ""Slot"" = 1"))
            .Should().Be(2);
        (await ScalarAsync<long>($@"
SELECT count(*) FROM ""ShiftImmOperatorAssignments""
WHERE ""ShiftPlanItemId"" = '{Item1Id}' AND ""ImmId"" = '{Imm1Id}' AND ""Slot"" = 1
  AND ""UnassignedAt"" IS NULL")).Should().Be(1);
    }

    [Fact]
    public async Task ActiveOperator_IsUniquePerImmButCanServeAnotherImmAndOnlyTwoSlotsExist()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);
        await InsertDayAndItemAsync(DayId, Item1Id);
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator1Id, 1));

        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator1Id, 2), "23505");
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item1Id, Imm2Id, Operator1Id, 1));

        await ExecuteSqlAsync(ItemSql(Item2Id, 2));
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item2Id, Imm1Id, Operator2Id, 1));
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item2Id, Imm1Id, Operator3Id, 2));

        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item2Id, Imm1Id, Operator4Id, 1), "23505");
        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item2Id, Imm1Id, Operator4Id, 3), "23514");
        (await ScalarAsync<long>($@"
SELECT count(*) FROM ""ShiftImmOperatorAssignments""
WHERE ""ShiftPlanItemId"" = '{Item2Id}' AND ""ImmId"" = '{Imm1Id}' AND ""UnassignedAt"" IS NULL"))
            .Should().Be(2);
    }

    [Fact]
    public async Task CheckConstraints_RejectInvalidSlotsUnassignmentAndItemInterval()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);
        await InsertDayAndItemAsync(DayId, Item1Id);

        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator1Id, 0), "23514");
        await ExpectSqlStateAsync(
            AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator1Id, 3), "23514");
        await ExpectSqlStateAsync($@"
INSERT INTO ""ShiftImmOperatorAssignments""
    (""Id"",""AssignedAt"",""CreatedAt"",""ImmId"",""OperatorId"",""ShiftPlanItemId"",""Slot"",""UnassignedAt"")
VALUES ('{Guid.NewGuid()}',TIMESTAMPTZ '2026-11-08T06:00:00Z',now(),'{Imm1Id}','{Operator1Id}',
        '{Item1Id}',1,TIMESTAMPTZ '2026-11-08T05:59:59Z')", "23514");
        await ExpectSqlStateAsync($@"
INSERT INTO ""ShiftPlanItems""
    (""Id"",""CreatedAt"",""EndAtUtc"",""Position"",""ShiftLeadId"",""ShiftPlanDayId"",""StartAtUtc"",""TimeZoneId"")
VALUES ('{Guid.NewGuid()}',now(),TIMESTAMPTZ '2026-11-08T06:00:00Z',2,NULL,'{DayId}',
        TIMESTAMPTZ '2026-11-08T06:00:00Z','Europe/Moscow')", "23514");
    }

    [Fact]
    public async Task Position_AllowsOnlyOneLeadButLeadCanHaveDifferentPositions()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);
        await InsertDayAndItemAsync(DayId, Item1Id, Lead1Id);

        await ExpectSqlStateAsync(ItemSql(Item2Id, 1, Lead2Id), "23505");
        await ExecuteSqlAsync(ItemSql(Item2Id, 2, Lead1Id));

        (await ScalarAsync<long>($@"
SELECT count(*) FROM ""ShiftPlanItems""
WHERE ""ShiftPlanDayId"" = '{DayId}' AND ""ShiftLeadId"" = '{Lead1Id}'")).Should().Be(2);
    }

    [Fact]
    public async Task Down_DropsOnlyNewTablesAndPreservesReferenceData()
    {
        await using var db = NewContext();
        var previous = await MigrateToTargetAsync(db);
        await InsertDayAndItemAsync(DayId, Item1Id, Lead1Id);
        await ExecuteSqlAsync(AssignmentSql(Guid.NewGuid(), Item1Id, Imm1Id, Operator1Id, 1));

        await db.GetService<IMigrator>().MigrateAsync(previous);

        foreach (var table in new[] { "ShiftPlanDays", "ShiftPlanItems", "ShiftImmOperatorAssignments" })
            (await ScalarAsync<string?>($"SELECT to_regclass('public.\"{table}\"')::text")).Should().BeNull();

        (await ScalarAsync<int>($@"SELECT ""StartMinutes"" FROM ""Shifts"" WHERE ""Id"" = '{ShiftId}'"))
            .Should().Be(480);
        (await ScalarAsync<string>($@"SELECT ""FullName"" FROM ""Users"" WHERE ""Id"" = '{Lead1Id}'"))
            .Should().Be(Lead1Id);
        (await ScalarAsync<string>($@"SELECT ""Name"" FROM ""Imms"" WHERE ""Id"" = '{Imm1Id}'"))
            .Should().Be("ТПА-1");
    }

    private async Task<string> MigrateToTargetAsync(ControlDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_Pzp32ShiftLeadStaffing"));
        var targetIndex = all.IndexOf(target);
        targetIndex.Should().BeGreaterThan(0);
        var previous = all[targetIndex - 1];
        await migrator.MigrateAsync(previous);
        await SeedReferenceDataAsync();
        await migrator.MigrateAsync(target);
        return previous;
    }

    private async Task SeedReferenceDataAsync()
    {
        await ExecuteSqlAsync($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES ('{TemplateId}','t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
VALUES ('{Imm1Id}',now(),TRUE,'ТПА-1','{TemplateId}'),
       ('{Imm2Id}',now(),TRUE,'ТПА-2','{TemplateId}');
INSERT INTO ""Shifts"" (""Id"",""BreakDurationMinutes"",""BreakStartMinutes"",""CreatedAt"",""DurationMinutes"",""StartMinutes"",""TimeZoneId"")
VALUES ('{ShiftId}',30,720,now(),720,480,'Europe/Moscow');
INSERT INTO ""Users""
    (""Id"",""AccessFailedCount"",""EmailConfirmed"",""FullName"",""IsActive"",""LockoutEnabled"",""PhoneNumberConfirmed"",""Role"",""TwoFactorEnabled"",""UserName"")
VALUES
    ('{Lead1Id}',0,FALSE,'{Lead1Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Lead2Id}',0,FALSE,'{Lead2Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator1Id}',0,FALSE,'{Operator1Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator2Id}',0,FALSE,'{Operator2Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator3Id}',0,FALSE,'{Operator3Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator4Id}',0,FALSE,'{Operator4Id}',TRUE,FALSE,FALSE,0,FALSE,NULL);");
    }

    private Task InsertDayAndItemAsync(Guid dayId, Guid itemId, string? leadId = null) =>
        ExecuteSqlAsync($@"
INSERT INTO ""ShiftPlanDays"" (""Id"",""CreatedAt"",""ShiftDate"")
VALUES ('{dayId}',now(),DATE '2026-11-08');
{ItemSql(itemId, 1, leadId, dayId)}");

    private static string ItemSql(Guid itemId, int position, string? leadId = null, Guid? dayId = null) => $@"
INSERT INTO ""ShiftPlanItems""
    (""Id"",""CreatedAt"",""EndAtUtc"",""Position"",""ShiftLeadId"",""ShiftPlanDayId"",""StartAtUtc"",""TimeZoneId"")
VALUES ('{itemId}',now(),TIMESTAMPTZ '2026-11-08T16:00:00Z',{position},{(leadId is null ? "NULL" : $"'{leadId}'")},
        '{dayId ?? DayId}',TIMESTAMPTZ '2026-11-08T06:00:00Z','Europe/Moscow')";

    private static string AssignmentSql(Guid id, Guid itemId, Guid immId, string operatorId, int slot) => $@"
INSERT INTO ""ShiftImmOperatorAssignments""
    (""Id"",""AssignedAt"",""CreatedAt"",""ImmId"",""OperatorId"",""ShiftPlanItemId"",""Slot"",""UnassignedAt"")
VALUES ('{id}',TIMESTAMPTZ '2026-11-08T06:00:00Z',now(),'{immId}','{operatorId}','{itemId}',{slot},NULL)";

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? default! : (T)value!;
    }

    private async Task ExpectSqlStateAsync(string sql, string sqlState)
    {
        var assertion = await FluentActions.Awaiting(() => ExecuteSqlAsync(sql))
            .Should().ThrowAsync<PostgresException>();
        assertion.Which.SqlState.Should().Be(sqlState);
    }
}
