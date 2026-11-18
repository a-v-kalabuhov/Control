using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>PZP-31: типизированный снимок сменного рапорта и его целостность в PostgreSQL.</summary>
public class Pzp31ShiftClosuresMigrationTests : IAsyncLifetime
{
    private static readonly Guid TemplateId = Guid.Parse("33000000-0000-0000-0000-000000000001");
    private static readonly Guid ImmId = Guid.Parse("33000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductTypeId = Guid.Parse("33000000-0000-0000-0000-000000000003");
    private static readonly Guid ProductId = Guid.Parse("33000000-0000-0000-0000-000000000004");
    private static readonly Guid MoldId = Guid.Parse("33000000-0000-0000-0000-000000000005");
    private static readonly Guid TaskId = Guid.Parse("33000000-0000-0000-0000-000000000006");
    private static readonly Guid DayId = Guid.Parse("33000000-0000-0000-0000-000000000010");
    private static readonly Guid ItemId = Guid.Parse("33000000-0000-0000-0000-000000000011");
    private static readonly Guid Item2Id = Guid.Parse("33000000-0000-0000-0000-000000000012");

    private const string CreatedById = "pzp31-closure-created-by";
    private const string ClosedById = "pzp31-closure-closed-by";
    private const string ChangedById = "pzp31-closure-changed-by";
    private const string Operator1Id = "pzp31-closure-operator-1";
    private const string Operator2Id = "pzp31-closure-operator-2";

    private static readonly string[] ClosureTables =
    {
        "ShiftClosures", "ShiftClosureEntries", "ShiftClosureOutputShares", "ShiftClosureCorrections"
    };

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_creates_exact_typed_snapshot_schema_and_cycle_index()
    {
        await MigrateToLatestAsync();

        var closureColumns = await GetColumnsAsync("ShiftClosures");
        closureColumns.Should().BeEquivalentTo(new Dictionary<string, (string Type, bool Nullable)>
        {
            ["Id"] = ("uuid", false),
            ["CreatedAt"] = ("timestamp with time zone", false),
            ["ShiftPlanItemId"] = ("uuid", false),
            ["State"] = ("integer", false),
            ["CreatedById"] = ("text", false),
            ["ClosedById"] = ("text", true),
            ["ClosedAt"] = ("timestamp with time zone", true)
        });

        var entryColumns = await GetColumnsAsync("ShiftClosureEntries");
        entryColumns.Should().BeEquivalentTo(new Dictionary<string, (string Type, bool Nullable)>
        {
            ["Id"] = ("uuid", false),
            ["CreatedAt"] = ("timestamp with time zone", false),
            ["ShiftClosureId"] = ("uuid", false),
            ["ShiftTaskId"] = ("uuid", false),
            ["ImmId"] = ("uuid", false),
            ["ImmNameSnapshot"] = ("text", false),
            ["ProductNameSnapshot"] = ("text", false),
            ["PlasticIngredientsSnapshot"] = ("text", false),
            ["ColorantIngredientsSnapshot"] = ("text", false),
            ["RemainingAtStart"] = ("integer", false),
            ["NormPartWeightGrams"] = ("numeric", false),
            ["PlannedFullCycleSeconds"] = ("integer", true),
            ["TaskPackagingUnit"] = ("integer", true),
            ["Cavities"] = ("integer", false),
            ["ShiftPlanQuantity"] = ("integer", false),
            ["PlannedMaterialWeightGrams"] = ("numeric", false),
            ["AutomaticOutputQuantity"] = ("integer", false),
            ["AutomaticAverageFullCycleSeconds"] = ("double precision", true),
            ["IsManualFallbackRequired"] = ("boolean", false),
            ["ActualPartWeightGrams"] = ("numeric", true),
            ["ManualFullCycleSeconds"] = ("integer", true),
            ["ActualPackagingUnit"] = ("integer", true),
            ["PackagingPlaces"] = ("numeric", true),
            ["ManualGoodQuantity"] = ("integer", true),
            ["ManualDefectQuantity"] = ("integer", true)
        });

        var shareColumns = await GetColumnsAsync("ShiftClosureOutputShares");
        shareColumns.Should().BeEquivalentTo(new Dictionary<string, (string Type, bool Nullable)>
        {
            ["Id"] = ("uuid", false),
            ["CreatedAt"] = ("timestamp with time zone", false),
            ["ShiftClosureEntryId"] = ("uuid", false),
            ["IntervalStartUtc"] = ("timestamp with time zone", false),
            ["IntervalEndUtc"] = ("timestamp with time zone", false),
            ["OperatorId"] = ("text", false),
            ["OperatorFullNameSnapshot"] = ("text", false),
            ["OperatorEmployeeIdSnapshot"] = ("text", true),
            ["SlotAtIntervalStart"] = ("integer", false),
            ["Quantity"] = ("integer", false)
        });

        var correctionColumns = await GetColumnsAsync("ShiftClosureCorrections");
        correctionColumns.Should().BeEquivalentTo(new Dictionary<string, (string Type, bool Nullable)>
        {
            ["Id"] = ("uuid", false),
            ["CreatedAt"] = ("timestamp with time zone", false),
            ["ShiftClosureId"] = ("uuid", false),
            ["ChangedById"] = ("text", false),
            ["ChangedAt"] = ("timestamp with time zone", false),
            ["Reason"] = ("text", false),
            ["ChangesJson"] = ("jsonb", false)
        });

        (await ScalarAsync<long>(@"
SELECT count(*) FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = ANY(@tables)
  AND data_type = 'jsonb' AND NOT (table_name = 'ShiftClosureCorrections' AND column_name = 'ChangesJson')",
            ("tables", ClosureTables))).Should().Be(0);
        (await ScalarAsync<long>(@"
SELECT count(*) FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = ANY(@tables) AND column_name = 'ClosedEarly'",
            ("tables", ClosureTables))).Should().Be(0);

        foreach (var column in new[] { "PlannedMaterialWeightGrams", "PackagingPlaces" })
        {
            var precision = await ScalarAsync<int?>($@"
SELECT numeric_precision FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'ShiftClosureEntries' AND column_name = '{column}'");
            var scale = await ScalarAsync<int?>($@"
SELECT numeric_scale FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'ShiftClosureEntries' AND column_name = '{column}'");
            precision.Should().Be(18);
            scale.Should().Be(2);
        }

        foreach (var column in new[] { "NormPartWeightGrams", "ActualPartWeightGrams" })
        {
            (await ScalarAsync<int?>($@"
SELECT numeric_precision FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'ShiftClosureEntries' AND column_name = '{column}'"))
                .Should().BeNull("веса используют тот же неограниченный numeric, что и ShiftTask.PartWeightGrams");
        }

        var cycleIndex = await ScalarAsync<string?>(@"
SELECT indexdef FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'ImmCycles' AND indexname = 'IX_ImmCycles_TaskId_EndTime'");
        cycleIndex.Should().NotBeNull();
        cycleIndex.Should().StartWith("CREATE INDEX ");
        cycleIndex.Should().Contain("(\"TaskId\", \"EndTime\")");
        (await ScalarAsync<long>(@"
SELECT count(*) FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'ImmCycles' AND indexname = 'IX_ImmCycles_TaskId'"))
            .Should().Be(1, "новый составной индекс не должен удалять существующий индекс TaskId");
    }

    [Fact]
    public async Task Unique_keys_allow_task_across_shifts_but_reject_duplicate_rows()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();
        var entryId = await InsertEntryAsync(closureId);

        await ExpectSqlStateAsync(ClosureSql(Guid.NewGuid(), ItemId), "23505");
        await ExpectSqlStateAsync(EntrySql(Guid.NewGuid(), closureId), "23505");

        var secondClosureId = await InsertClosureAsync(itemId: Item2Id);
        await InsertEntryAsync(secondClosureId);

        await InsertShareAsync(entryId);
        await ExpectSqlStateAsync(ShareSql(Guid.NewGuid(), entryId), "23505");
        await InsertShareAsync(entryId, operatorId: Operator2Id);
    }

    [Fact]
    public async Task Entry_checks_accept_null_manual_values_and_reject_out_of_range_values()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();
        var entryId = await InsertEntryAsync(closureId);

        await ExecuteSqlAsync($@"
UPDATE ""ShiftClosureEntries"" SET ""NormPartWeightGrams"" = 0 WHERE ""Id"" = '{entryId}'");

        var invalidValues = new Dictionary<string, string>
        {
            ["ActualPartWeightGrams"] = "0",
            ["PlannedFullCycleSeconds"] = "0",
            ["ManualFullCycleSeconds"] = "0",
            ["TaskPackagingUnit"] = "0",
            ["ActualPackagingUnit"] = "0",
            ["PackagingPlaces"] = "-0.01",
            ["ManualGoodQuantity"] = "-1",
            ["ManualDefectQuantity"] = "-1"
        };
        foreach (var (column, value) in invalidValues)
        {
            await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosureEntries"" SET ""{column}"" = {value} WHERE ""Id"" = '{entryId}'", "23514");
        }

        await ExecuteSqlAsync($@"
UPDATE ""ShiftClosureEntries""
SET ""ActualPartWeightGrams"" = 10.5, ""ManualFullCycleSeconds"" = 62,
    ""ActualPackagingUnit"" = 12, ""PackagingPlaces"" = 1.25,
    ""ManualGoodQuantity"" = 0, ""ManualDefectQuantity"" = 0
WHERE ""Id"" = '{entryId}'");
        (await ScalarAsync<decimal>($@"
SELECT ""PackagingPlaces"" FROM ""ShiftClosureEntries"" WHERE ""Id"" = '{entryId}'"))
            .Should().Be(1.25m);
    }

    [Fact]
    public async Task Closure_state_requires_matching_audit_fields()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();

        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosures"" SET ""State"" = 1 WHERE ""Id"" = '{closureId}'", "23514");
        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosures""
SET ""State"" = 1, ""ClosedAt"" = TIMESTAMPTZ '2026-11-18T15:00:00Z'
WHERE ""Id"" = '{closureId}'", "23514");
        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosures""
SET ""State"" = 1, ""ClosedById"" = '{ClosedById}'
WHERE ""Id"" = '{closureId}'", "23514");

        await ExecuteSqlAsync($@"
UPDATE ""ShiftClosures""
SET ""State"" = 1, ""ClosedById"" = '{ClosedById}', ""ClosedAt"" = TIMESTAMPTZ '2026-11-18T15:00:00Z'
WHERE ""Id"" = '{closureId}'");
        var closedAt = await ScalarAsync<DateTime>($@"
SELECT ""ClosedAt"" FROM ""ShiftClosures"" WHERE ""Id"" = '{closureId}'");
        closedAt.Kind.Should().Be(DateTimeKind.Utc);
        closedAt.Should().Be(DateTime.Parse("2026-11-18T15:00:00Z").ToUniversalTime());

        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosures"" SET ""ClosedById"" = NULL WHERE ""Id"" = '{closureId}'", "23514");
        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosures"" SET ""State"" = 0 WHERE ""Id"" = '{closureId}'", "23514");
    }

    [Fact]
    public async Task Output_share_and_correction_rules_are_enforced()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();
        var entryId = await InsertEntryAsync(closureId);
        var shareId = await InsertShareAsync(entryId);

        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosureOutputShares"" SET ""SlotAtIntervalStart"" = 0 WHERE ""Id"" = '{shareId}'", "23514");
        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosureOutputShares"" SET ""SlotAtIntervalStart"" = 3 WHERE ""Id"" = '{shareId}'", "23514");
        await ExpectSqlStateAsync($@"
UPDATE ""ShiftClosureOutputShares"" SET ""Quantity"" = -1 WHERE ""Id"" = '{shareId}'", "23514");

        var correctionId = Guid.NewGuid();
        await ExecuteSqlAsync(CorrectionSql(correctionId, closureId, "'   '"));
        var changedAt = await ScalarAsync<DateTime>($@"
SELECT ""ChangedAt"" FROM ""ShiftClosureCorrections"" WHERE ""Id"" = '{correctionId}'");
        changedAt.Kind.Should().Be(DateTimeKind.Utc);
        (await ScalarAsync<string>($@"
SELECT ""ChangesJson"" FROM ""ShiftClosureCorrections"" WHERE ""Id"" = '{correctionId}'"))
            .Should().Be("[]");
        await ExpectSqlStateAsync(CorrectionSql(Guid.NewGuid(), closureId, "NULL"), "23502");
    }

    [Fact]
    public async Task Foreign_keys_restrict_references_and_cascade_closure_children()
    {
        await MigrateToLatestAsync();
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();
        var entryId = await InsertEntryAsync(closureId);
        await InsertShareAsync(entryId);
        await ExecuteSqlAsync(CorrectionSql(Guid.NewGuid(), closureId, "'Причина'"));
        await ExecuteSqlAsync($@"
UPDATE ""ShiftClosures""
SET ""State"" = 1, ""ClosedById"" = '{ClosedById}', ""ClosedAt"" = now()
WHERE ""Id"" = '{closureId}'");

        var expectedRestrict = new[]
        {
            "FK_ShiftClosures_Users_CreatedById",
            "FK_ShiftClosures_Users_ClosedById",
            "FK_ShiftClosureEntries_ShiftTasks_ShiftTaskId",
            "FK_ShiftClosureEntries_Imms_ImmId",
            "FK_ShiftClosureOutputShares_Users_OperatorId",
            "FK_ShiftClosureCorrections_Users_ChangedById"
        };
        foreach (var name in expectedRestrict)
            (await ForeignKeyDeleteActionAsync(name)).Should().Be("r", name);

        var expectedCascade = new[]
        {
            "FK_ShiftClosureEntries_ShiftClosures_ShiftClosureId",
            "FK_ShiftClosureOutputShares_ShiftClosureEntries_ShiftClosureEn~",
            "FK_ShiftClosureCorrections_ShiftClosures_ShiftClosureId"
        };
        foreach (var name in expectedCascade)
            (await ForeignKeyDeleteActionAsync(name)).Should().Be("c", name);

        foreach (var userId in new[] { CreatedById, ClosedById, ChangedById, Operator1Id })
            await ExpectSqlStateAsync($@"DELETE FROM ""Users"" WHERE ""Id"" = '{userId}'", "23503");
        await ExpectSqlStateAsync($@"DELETE FROM ""ShiftTasks"" WHERE ""Id"" = '{TaskId}'", "23503");
        await ExpectSqlStateAsync($@"DELETE FROM ""Imms"" WHERE ""Id"" = '{ImmId}'", "23503");

        await ExecuteSqlAsync($@"DELETE FROM ""ShiftClosures"" WHERE ""Id"" = '{closureId}'");
        foreach (var table in new[] { "ShiftClosureEntries", "ShiftClosureOutputShares", "ShiftClosureCorrections" })
            (await ScalarAsync<long>($@"SELECT count(*) FROM ""{table}""")).Should().Be(0);
        (await ScalarAsync<long>($@"SELECT count(*) FROM ""ShiftPlanItems"" WHERE ""Id"" = '{ItemId}'"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Down_drops_only_closure_objects_and_cycle_index()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.SingleOrDefault(m => m.EndsWith("_Pzp31ShiftClosures"));
        target.Should().NotBeNull("миграция Pzp31ShiftClosures должна существовать");
        var targetIndex = all.IndexOf(target!);
        targetIndex.Should().BeGreaterThan(0);
        var previous = all[targetIndex - 1];
        await migrator.MigrateAsync(target);
        await SeedReferenceDataAsync();
        var closureId = await InsertClosureAsync();
        await InsertEntryAsync(closureId);

        await migrator.MigrateAsync(previous);

        foreach (var table in ClosureTables)
            (await ScalarAsync<string?>($"SELECT to_regclass('public.\"{table}\"')::text")).Should().BeNull();
        (await ScalarAsync<string?>(@"
SELECT indexname FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'ImmCycles' AND indexname = 'IX_ImmCycles_TaskId_EndTime'"))
            .Should().BeNull();
        (await ScalarAsync<string>($@"SELECT ""FullName"" FROM ""Users"" WHERE ""Id"" = '{CreatedById}'"))
            .Should().Be(CreatedById);
        (await ScalarAsync<string>($@"SELECT ""Name"" FROM ""Imms"" WHERE ""Id"" = '{ImmId}'"))
            .Should().Be("ТПА-1");
        (await ScalarAsync<long>(@"
SELECT count(*) FROM pg_indexes
WHERE schemaname = 'public' AND tablename = 'ImmCycles' AND indexname = 'IX_ImmCycles_TaskId'"))
            .Should().Be(1);
        (await ScalarAsync<long>(@"
SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'")).Should().Be(1);
    }

    private async Task MigrateToLatestAsync()
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync();
    }

    private Task SeedReferenceDataAsync() => ExecuteSqlAsync($@"
INSERT INTO ""Templates"" (""Id"",""Author"",""CreatedAt"",""IsActive"",""JsonConfig"",""Manufacturer"",""Model"",""Name"",""UpdatedAt"",""Version"")
VALUES ('{TemplateId}','t',now(),TRUE,'{{}}','m','m','tpl',now(),'1');
INSERT INTO ""Imms"" (""Id"",""CreatedAt"",""IsActive"",""Name"",""TemplateId"")
VALUES ('{ImmId}',now(),TRUE,'ТПА-1','{TemplateId}');
INSERT INTO ""ProductTypes"" (""Id"",""Article"",""CreatedAt"",""IsActive"",""Name"")
VALUES ('{ProductTypeId}','CLOSURE',now(),TRUE,'Сменный рапорт');
INSERT INTO ""Products""
    (""Id"",""Article"",""CreatedAt"",""FullArticle"",""IsActive"",""Name"",""PartWeightGrams"",""ProductTypeId"",""RunnerWeightGrams"")
VALUES ('{ProductId}','0',now(),'CLOSURE.0',TRUE,'Деталь',10,'{ProductTypeId}',1);
INSERT INTO ""Molds""
    (""Id"",""Cavities"",""CreatedAt"",""FormId"",""HasMovingParts"",""IsActive"",""MaxResourceCycles"",""Name"",""ProductTypeId"",""RequiresConveyor"",""RequiresRobot"")
VALUES ('{MoldId}',2,now(),'CLOSURE-1',FALSE,TRUE,100000,'Форма','{ProductTypeId}',FALSE,FALSE);
INSERT INTO ""ShiftTasks""
    (""Id"",""ActualMaterialWeightGrams"",""ActualQuantity"",""Cavities"",""CreatedAt"",""DefectQuantity"",
     ""ImmId"",""MoldId"",""PartWeightGrams"",""PlanQuantity"",""PlannedDate"",""PlannedFullCycleSeconds"",
     ""PlannedSetupMinutes"",""ProductId"",""ReleaseBufferFixedMinutes"",""ReleaseBufferPercent"",""Status"",""WorkMode"")
VALUES ('{TaskId}',0,0,2,now(),0,'{ImmId}','{MoldId}',10,100,DATE '2026-11-18',60,
        0,'{ProductId}',0,0,3,0);
INSERT INTO ""Users""
    (""Id"",""AccessFailedCount"",""EmailConfirmed"",""FullName"",""IsActive"",""LockoutEnabled"",""PhoneNumberConfirmed"",""Role"",""TwoFactorEnabled"",""UserName"")
VALUES
    ('{CreatedById}',0,FALSE,'{CreatedById}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{ClosedById}',0,FALSE,'{ClosedById}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{ChangedById}',0,FALSE,'{ChangedById}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator1Id}',0,FALSE,'{Operator1Id}',TRUE,FALSE,FALSE,0,FALSE,NULL),
    ('{Operator2Id}',0,FALSE,'{Operator2Id}',TRUE,FALSE,FALSE,0,FALSE,NULL);
INSERT INTO ""ShiftPlanDays"" (""Id"",""CreatedAt"",""ShiftDate"")
VALUES ('{DayId}',now(),DATE '2026-11-18');
INSERT INTO ""ShiftPlanItems""
    (""Id"",""CreatedAt"",""EndAtUtc"",""Position"",""ShiftLeadId"",""ShiftPlanDayId"",""StartAtUtc"",""TimeZoneId"")
VALUES ('{ItemId}',now(),TIMESTAMPTZ '2026-11-18T16:00:00Z',1,'{CreatedById}','{DayId}',
        TIMESTAMPTZ '2026-11-18T06:00:00Z','Europe/Moscow'),
       ('{Item2Id}',now(),TIMESTAMPTZ '2026-11-19T04:00:00Z',2,'{CreatedById}','{DayId}',
        TIMESTAMPTZ '2026-11-18T18:00:00Z','Europe/Moscow');");

    private async Task<Guid> InsertClosureAsync(Guid? closureId = null, Guid? itemId = null)
    {
        var id = closureId ?? Guid.NewGuid();
        await ExecuteSqlAsync(ClosureSql(id, itemId ?? ItemId));
        return id;
    }

    private static string ClosureSql(Guid id, Guid itemId) => $@"
INSERT INTO ""ShiftClosures""
    (""Id"",""ClosedAt"",""ClosedById"",""CreatedAt"",""CreatedById"",""ShiftPlanItemId"",""State"")
VALUES ('{id}',NULL,NULL,now(),'{CreatedById}','{itemId}',0)";

    private async Task<Guid> InsertEntryAsync(Guid closureId, Guid? entryId = null)
    {
        var id = entryId ?? Guid.NewGuid();
        await ExecuteSqlAsync(EntrySql(id, closureId));
        return id;
    }

    private static string EntrySql(Guid id, Guid closureId) => $@"
INSERT INTO ""ShiftClosureEntries""
    (""Id"",""AutomaticAverageFullCycleSeconds"",""AutomaticOutputQuantity"",""Cavities"",
     ""ColorantIngredientsSnapshot"",""CreatedAt"",""ImmId"",""ImmNameSnapshot"",""IsManualFallbackRequired"",
     ""NormPartWeightGrams"",""PlannedFullCycleSeconds"",""PlannedMaterialWeightGrams"",
     ""PlasticIngredientsSnapshot"",""ProductNameSnapshot"",""RemainingAtStart"",""ShiftClosureId"",
     ""ShiftPlanQuantity"",""ShiftTaskId"",""TaskPackagingUnit"")
VALUES ('{id}',61.5,20,2,'Краситель',now(),'{ImmId}','ТПА-1',FALSE,
        10,60,1000,'Пластик','Деталь',80,'{closureId}',100,'{TaskId}',10)";

    private async Task<Guid> InsertShareAsync(Guid entryId, Guid? shareId = null, string operatorId = Operator1Id)
    {
        var id = shareId ?? Guid.NewGuid();
        await ExecuteSqlAsync(ShareSql(id, entryId, operatorId));
        return id;
    }

    private static string ShareSql(Guid id, Guid entryId, string operatorId = Operator1Id) => $@"
INSERT INTO ""ShiftClosureOutputShares""
    (""Id"",""CreatedAt"",""IntervalEndUtc"",""IntervalStartUtc"",""OperatorEmployeeIdSnapshot"",
     ""OperatorFullNameSnapshot"",""OperatorId"",""Quantity"",""ShiftClosureEntryId"",""SlotAtIntervalStart"")
VALUES ('{id}',now(),TIMESTAMPTZ '2026-11-18T09:00:00Z',TIMESTAMPTZ '2026-11-18T08:00:00Z',NULL,
        'Литейщик','{operatorId}',5,'{entryId}',1)";

    private static string CorrectionSql(Guid id, Guid closureId, string reasonSql) => $@"
INSERT INTO ""ShiftClosureCorrections""
    (""Id"",""ChangedAt"",""ChangedById"",""ChangesJson"",""CreatedAt"",""Reason"",""ShiftClosureId"")
VALUES ('{id}',TIMESTAMPTZ '2026-11-18T15:30:00Z','{ChangedById}','[]'::jsonb,now(),{reasonSql},'{closureId}')";

    private async Task<Dictionary<string, (string Type, bool Nullable)>> GetColumnsAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT column_name, data_type, is_nullable FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = @table", conn);
        cmd.Parameters.AddWithValue("table", table);
        var result = new Dictionary<string, (string Type, bool Nullable)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0), (reader.GetString(1), reader.GetString(2) == "YES"));
        return result;
    }

    private async Task<string?> ForeignKeyDeleteActionAsync(string name) =>
        await ScalarAsync<string?>(@"SELECT confdeltype::text FROM pg_constraint WHERE conname = @name",
            ("name", name));

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, parameterValue) in parameters)
            cmd.Parameters.AddWithValue(name, parameterValue);
        var value = await cmd.ExecuteScalarAsync();
        return value is DBNull or null ? default! : (T)value;
    }

    private async Task ExpectSqlStateAsync(string sql, string sqlState)
    {
        var assertion = await FluentActions.Awaiting(() => ExecuteSqlAsync(sql))
            .Should().ThrowAsync<PostgresException>();
        assertion.Which.SqlState.Should().Be(sqlState);
    }
}
