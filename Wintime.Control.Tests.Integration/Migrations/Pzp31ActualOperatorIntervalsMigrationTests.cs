using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>PZP-31: аудит и полуоткрытые временные ограничения назначений литейщиков.</summary>
public class Pzp31ActualOperatorIntervalsMigrationTests : IAsyncLifetime
{
    private static readonly Guid TemplateId = Guid.Parse("31000000-0000-0000-0000-000000000001");
    private static readonly Guid Imm1Id = Guid.Parse("31000000-0000-0000-0000-000000000002");
    private static readonly Guid Imm2Id = Guid.Parse("31000000-0000-0000-0000-000000000003");
    private static readonly Guid DayId = Guid.Parse("31000000-0000-0000-0000-000000000010");
    private static readonly Guid ItemId = Guid.Parse("31000000-0000-0000-0000-000000000011");

    private const string Lead1Id = "pzp31-lead-1";
    private const string Lead2Id = "pzp31-lead-2";
    private const string Operator1Id = "pzp31-operator-1";
    private const string Operator2Id = "pzp31-operator-2";
    private const string Operator3Id = "pzp31-operator-3";
    private const string Operator4Id = "pzp31-operator-4";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Different_operators_can_overlap_in_two_slots_but_third_does_not_fit()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        await InsertAsync(NewAssignment(1, "2026-11-18T08:00:00Z", "2026-11-18T12:00:00Z"));
        await InsertAsync(NewAssignment(2, "2026-11-18T09:00:00Z", "2026-11-18T13:00:00Z", Operator2Id));

        var third = NewAssignment(1, "2026-11-18T10:00:00Z", "2026-11-18T11:00:00Z", Operator3Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(third));
        error.SqlState.Should().Be("23P01");

        third = NewAssignment(2, "2026-11-18T10:00:00Z", "2026-11-18T11:00:00Z", Operator3Id);
        error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(third));
        error.SqlState.Should().Be("23P01");
    }

    [Fact]
    public async Task Same_slot_rejects_overlap_but_allows_adjacent_half_open_intervals()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        await InsertAsync(NewAssignment(1, "2026-11-18T10:00:00Z", "2026-11-18T11:00:00Z"));

        var overlapping = NewAssignment(1, "2026-11-18T10:30:00Z", "2026-11-18T11:30:00Z", Operator2Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(overlapping));
        error.SqlState.Should().Be("23P01");

        await InsertAsync(NewAssignment(1, "2026-11-18T11:00:00Z", "2026-11-18T12:00:00Z", Operator2Id));
    }

    [Fact]
    public async Task Same_operator_cannot_overlap_on_same_imm_but_can_on_another_imm()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var first = NewAssignment(1, "2026-11-18T08:00:00Z", "2026-11-18T12:00:00Z");
        var overlapping = NewAssignment(2, "2026-11-18T11:00:00Z", "2026-11-18T13:00:00Z");
        await InsertAsync(first);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(overlapping));
        error.SqlState.Should().Be("23P01");

        overlapping.ImmId = Imm2Id;
        await InsertAsync(overlapping);
    }

    [Fact]
    public async Task Empty_range_does_not_conflict_and_reversed_range_still_violates_check()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        await InsertAsync(NewAssignment(1, "2026-11-18T08:00:00Z", "2026-11-18T12:00:00Z"));
        await InsertAsync(NewAssignment(1, "2026-11-18T09:00:00Z", "2026-11-18T09:00:00Z"));

        var reversed = NewAssignment(2, "2026-11-18T10:00:00Z", "2026-11-18T09:59:59Z", Operator2Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(reversed));
        error.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task Up_backfills_created_by_from_shift_lead_and_preserves_creation_time()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);
        await migrator.MigrateAsync(previous);
        await SeedReferenceDataAsync();
        var assignmentId = Guid.NewGuid();
        var createdAt = Utc("2026-11-17T18:00:00Z");
        await ExecuteSqlAsync($@"
INSERT INTO ""ShiftImmOperatorAssignments""
    (""Id"",""AssignedAt"",""CreatedAt"",""ImmId"",""OperatorId"",""ShiftPlanItemId"",""Slot"",""UnassignedAt"")
VALUES ('{assignmentId}',TIMESTAMPTZ '2026-11-18T08:00:00Z',TIMESTAMPTZ '2026-11-17T18:00:00Z',
        '{Imm1Id}','{Operator1Id}','{ItemId}',1,TIMESTAMPTZ '2026-11-18T09:00:00Z')");

        await migrator.MigrateAsync(target);

        (await ScalarAsync<string>($@"SELECT ""CreatedById"" FROM ""ShiftImmOperatorAssignments"" WHERE ""Id"" = '{assignmentId}'"))
            .Should().Be(Lead1Id);
        var actualCreatedAt = await ScalarAsync<DateTime>(
            $@"SELECT ""CreatedAt"" FROM ""ShiftImmOperatorAssignments"" WHERE ""Id"" = '{assignmentId}'");
        actualCreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        actualCreatedAt.Should().Be(createdAt);
        (await ScalarAsync<string?>($@"SELECT ""UpdatedById"" FROM ""ShiftImmOperatorAssignments"" WHERE ""Id"" = '{assignmentId}'"))
            .Should().BeNull();
        (await ScalarAsync<DateTime?>($@"SELECT ""UpdatedAt"" FROM ""ShiftImmOperatorAssignments"" WHERE ""Id"" = '{assignmentId}'"))
            .Should().BeNull();
    }

    [Fact]
    public async Task Created_by_is_required_and_update_audit_columns_are_nullable()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();

        (await ScalarAsync<string>(@"SELECT is_nullable FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'ShiftImmOperatorAssignments' AND column_name = 'CreatedById'"))
            .Should().Be("NO");
        (await ScalarAsync<long>(@"SELECT count(*) FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'ShiftImmOperatorAssignments'
  AND column_name IN ('UpdatedById', 'UpdatedAt') AND is_nullable = 'YES'"))
            .Should().Be(2);

        await ExpectSqlStateAsync($@"
INSERT INTO ""ShiftImmOperatorAssignments""
    (""Id"",""AssignedAt"",""CreatedAt"",""CreatedById"",""ImmId"",""OperatorId"",""ShiftPlanItemId"",""Slot"",""UnassignedAt"")
VALUES ('{Guid.NewGuid()}',TIMESTAMPTZ '2026-11-18T08:00:00Z',now(),NULL,
        '{Imm1Id}','{Operator1Id}','{ItemId}',1,NULL)", "23502");
    }

    [Fact]
    public async Task Author_foreign_keys_restrict_deletion()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var assignment = NewAssignment(1, "2026-11-18T08:00:00Z", "2026-11-18T09:00:00Z");
        assignment.CreatedById = Operator3Id;
        await InsertAsync(assignment);
        await ExecuteSqlAsync($@"
UPDATE ""ShiftImmOperatorAssignments""
SET ""UpdatedById"" = '{Operator4Id}', ""UpdatedAt"" = TIMESTAMPTZ '2026-11-18T10:00:00Z'
WHERE ""Id"" = '{assignment.Id}'");

        (await ScalarAsync<long>(@"SELECT count(*) FROM pg_constraint
WHERE conrelid = '""ShiftImmOperatorAssignments""'::regclass AND contype = 'f' AND confdeltype = 'r'
  AND conname IN (
    'FK_ShiftImmOperatorAssignments_Users_CreatedById',
    'FK_ShiftImmOperatorAssignments_Users_UpdatedById')"))
            .Should().Be(2);
        await ExpectSqlStateAsync($@"DELETE FROM ""Users"" WHERE ""Id"" = '{Operator3Id}'", "23503");
        await ExpectSqlStateAsync($@"DELETE FROM ""Users"" WHERE ""Id"" = '{Operator4Id}'", "23503");
    }

    [Fact]
    public async Task Down_restores_partial_unique_indexes_and_keeps_btree_gist()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var (target, previous) = TargetAndPrevious(db);
        await migrator.MigrateAsync(target);

        (await ScalarAsync<long>(PartialIndexCountSql)).Should().Be(0);
        (await ScalarAsync<long>(ExclusionConstraintCountSql)).Should().Be(2);
        (await ScalarAsync<long>(BtreeGistCountSql)).Should().Be(1);

        await migrator.MigrateAsync(previous);

        (await ScalarAsync<long>(PartialIndexCountSql)).Should().Be(2);
        (await ScalarAsync<long>(ExclusionConstraintCountSql)).Should().Be(0);
        (await ScalarAsync<long>(BtreeGistCountSql)).Should().Be(1);
    }

    private const string PartialIndexCountSql = @"
SELECT count(*) FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'ShiftImmOperatorAssignments'
  AND indexname IN ('IX_ShiftImmOperatorAssignments_ActiveSlot', 'IX_ShiftImmOperatorAssignments_ActiveOperator')
  AND indexdef LIKE 'CREATE UNIQUE INDEX%' AND indexdef LIKE '%WHERE%IS NULL%'";

    private const string ExclusionConstraintCountSql = @"
SELECT count(*) FROM pg_constraint
WHERE conrelid = '""ShiftImmOperatorAssignments""'::regclass AND contype = 'x'
  AND conname IN ('EX_ShiftImmOperatorAssignments_SlotRange', 'EX_ShiftImmOperatorAssignments_OperatorRange')";

    private const string BtreeGistCountSql = "SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'";

    private async Task MigrateToLatestAsync()
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync();
    }

    private static (string Target, string Previous) TargetAndPrevious(ControlDbContext db)
    {
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_Pzp31ActualOperatorIntervals"));
        var targetIndex = all.IndexOf(target);
        targetIndex.Should().BeGreaterThan(0);
        return (target, all[targetIndex - 1]);
    }

    private Task SeedReferenceDataAsync() => ExecuteSqlAsync($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES ('{TemplateId}','t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
VALUES ('{Imm1Id}',now(),TRUE,'ТПА-1','{TemplateId}'),
       ('{Imm2Id}',now(),TRUE,'ТПА-2','{TemplateId}');
INSERT INTO ""Users""
    (""Id"",""AccessFailedCount"",""EmailConfirmed"",""FullName"",""IsActive"",""LockoutEnabled"",""PhoneNumberConfirmed"",""Role"",""TwoFactorEnabled"",""UserName"")
VALUES
    ('{Lead1Id}',0,FALSE,'{Lead1Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Lead2Id}',0,FALSE,'{Lead2Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator1Id}',0,FALSE,'{Operator1Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator2Id}',0,FALSE,'{Operator2Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator3Id}',0,FALSE,'{Operator3Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator4Id}',0,FALSE,'{Operator4Id}',TRUE,FALSE,FALSE,0,FALSE,NULL);
INSERT INTO ""ShiftPlanDays"" (""Id"",""CreatedAt"",""ShiftDate"")
VALUES ('{DayId}',now(),DATE '2026-11-18');
INSERT INTO ""ShiftPlanItems""
    (""Id"",""CreatedAt"",""EndAtUtc"",""Position"",""ShiftLeadId"",""ShiftPlanDayId"",""StartAtUtc"",""TimeZoneId"")
VALUES ('{ItemId}',now(),TIMESTAMPTZ '2026-11-18T16:00:00Z',1,'{Lead1Id}','{DayId}',
        TIMESTAMPTZ '2026-11-18T06:00:00Z','Europe/Moscow');");

    private static Assignment NewAssignment(
        int slot,
        string start,
        string? end = null,
        string operatorId = Operator1Id) => new()
    {
        Id = Guid.NewGuid(),
        ShiftPlanItemId = ItemId,
        ImmId = Imm1Id,
        OperatorId = operatorId,
        Slot = slot,
        AssignedAt = Utc(start),
        UnassignedAt = end is null ? null : Utc(end),
        CreatedAt = Utc("2026-11-18T07:00:00Z"),
        CreatedById = Lead1Id
    };

    private async Task InsertAsync(Assignment assignment)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
INSERT INTO ""ShiftImmOperatorAssignments""
    (""Id"",""AssignedAt"",""CreatedAt"",""CreatedById"",""ImmId"",""OperatorId"",
     ""ShiftPlanItemId"",""Slot"",""UnassignedAt"",""UpdatedAt"",""UpdatedById"")
VALUES (@id,@assignedAt,@createdAt,@createdById,@immId,@operatorId,
        @shiftPlanItemId,@slot,@unassignedAt,@updatedAt,@updatedById)", conn);
        AddParameter(cmd.Parameters, "id", NpgsqlDbType.Uuid, assignment.Id);
        AddParameter(cmd.Parameters, "assignedAt", NpgsqlDbType.TimestampTz, assignment.AssignedAt);
        AddParameter(cmd.Parameters, "createdAt", NpgsqlDbType.TimestampTz, assignment.CreatedAt);
        AddParameter(cmd.Parameters, "createdById", NpgsqlDbType.Text, assignment.CreatedById);
        AddParameter(cmd.Parameters, "immId", NpgsqlDbType.Uuid, assignment.ImmId);
        AddParameter(cmd.Parameters, "operatorId", NpgsqlDbType.Text, assignment.OperatorId);
        AddParameter(cmd.Parameters, "shiftPlanItemId", NpgsqlDbType.Uuid, assignment.ShiftPlanItemId);
        AddParameter(cmd.Parameters, "slot", NpgsqlDbType.Integer, assignment.Slot);
        AddParameter(cmd.Parameters, "unassignedAt", NpgsqlDbType.TimestampTz, assignment.UnassignedAt);
        AddParameter(cmd.Parameters, "updatedAt", NpgsqlDbType.TimestampTz, assignment.UpdatedAt);
        AddParameter(cmd.Parameters, "updatedById", NpgsqlDbType.Text, assignment.UpdatedById);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddParameter(
        NpgsqlParameterCollection parameters,
        string name,
        NpgsqlDbType type,
        object? value) =>
        parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static DateTime Utc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).UtcDateTime;

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
        return value is DBNull or null ? default! : (T)value;
    }

    private async Task ExpectSqlStateAsync(string sql, string sqlState)
    {
        var assertion = await FluentActions.Awaiting(() => ExecuteSqlAsync(sql))
            .Should().ThrowAsync<PostgresException>();
        assertion.Which.SqlState.Should().Be(sqlState);
    }

    private sealed class Assignment
    {
        public required Guid Id { get; init; }
        public required Guid ShiftPlanItemId { get; init; }
        public Guid ImmId { get; set; }
        public required string OperatorId { get; init; }
        public required int Slot { get; init; }
        public required DateTime AssignedAt { get; init; }
        public DateTime? UnassignedAt { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required string CreatedById { get; set; }
        public string? UpdatedById { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
