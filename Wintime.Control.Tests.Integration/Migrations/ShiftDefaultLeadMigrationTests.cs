using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

public class ShiftDefaultLeadMigrationTests : IAsyncLifetime
{
    private const string LeadId = "shift-default-lead-user";
    private const string OtherUserId = "shift-default-other-user";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_CreatesPositionKeyedDefaultsWithRestrictUserFkAndNoShiftFk()
    {
        await using var db = NewContext();
        await MigrateToTargetAsync(db);

        (await ScalarAsync<string?>("SELECT to_regclass('public.\"ShiftDefaultLeads\"')::text"))
            .Should().Be("\"ShiftDefaultLeads\"");
        (await ScalarAsync<string?>("SELECT to_regclass('public.\"IX_ShiftDefaultLeads_ShiftLeadId\"')::text"))
            .Should().Be("\"IX_ShiftDefaultLeads_ShiftLeadId\"");
        (await ScalarAsync<long>(@"
SELECT count(*) FROM pg_constraint
WHERE conrelid = '""ShiftDefaultLeads""'::regclass
  AND confrelid = '""Shifts""'::regclass AND contype = 'f'")).Should().Be(0);

        await ExecuteSqlAsync(DefaultSql(1, LeadId) + DefaultSql(2, LeadId));
        (await ScalarAsync<long>($"SELECT count(*) FROM \"ShiftDefaultLeads\" WHERE \"ShiftLeadId\" = '{LeadId}'"))
            .Should().Be(2, "один пользователь может быть старшим нескольких позиций");

        await ExpectSqlStateAsync(DefaultSql(1, OtherUserId), "23505");
        await ExpectSqlStateAsync(DefaultSql(0, OtherUserId), "23514");
        await ExpectSqlStateAsync(DefaultSql(3, "missing-shift-default-user"), "23503");
        await ExpectSqlStateAsync($"DELETE FROM \"Users\" WHERE \"Id\" = '{LeadId}'", "23503");

        (await ScalarAsync<long>($"SELECT count(*) FROM \"ShiftDefaultLeads\" WHERE \"ShiftLeadId\" = '{LeadId}'"))
            .Should().Be(2, "все нарушения ограничений откатываются без частичных изменений");
    }

    [Fact]
    public async Task Down_DropsDefaultsButPreservesUsers()
    {
        await using var db = NewContext();
        var previous = await MigrateToTargetAsync(db);
        await ExecuteSqlAsync(DefaultSql(1, LeadId));

        await db.GetService<IMigrator>().MigrateAsync(previous);

        (await ScalarAsync<string?>("SELECT to_regclass('public.\"ShiftDefaultLeads\"')::text")).Should().BeNull();
        (await ScalarAsync<string>($"SELECT \"FullName\" FROM \"Users\" WHERE \"Id\" = '{LeadId}'"))
            .Should().Be(LeadId);
    }

    private async Task<string> MigrateToTargetAsync(ControlDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.SingleOrDefault(m => m.EndsWith("_AddShiftDefaultLeads"));
        target.Should().NotBeNull("миграция AddShiftDefaultLeads должна существовать");
        var targetIndex = all.IndexOf(target!);
        targetIndex.Should().BeGreaterThan(0);
        var previous = all[targetIndex - 1];
        await migrator.MigrateAsync(previous);
        await SeedUsersAsync();
        await migrator.MigrateAsync(target);
        return previous;
    }

    private Task SeedUsersAsync() => ExecuteSqlAsync($@"
INSERT INTO ""Users""
    (""Id"",""AccessFailedCount"",""CreatedAt"",""EmailConfirmed"",""FullName"",""IsActive"",""LockoutEnabled"",""PhoneNumberConfirmed"",""Role"",""TwoFactorEnabled"",""UserName"")
VALUES
    ('{LeadId}',0,now(),FALSE,'{LeadId}',TRUE,FALSE,FALSE,6,FALSE,NULL),
    ('{OtherUserId}',0,now(),FALSE,'{OtherUserId}',TRUE,FALSE,FALSE,6,FALSE,NULL);");

    private static string DefaultSql(int position, string leadId) => $@"
INSERT INTO ""ShiftDefaultLeads"" (""Position"",""ShiftLeadId"")
VALUES ({position},'{leadId}');";

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExpectSqlStateAsync(string sql, string expected)
    {
        var action = async () => await ExecuteSqlAsync(sql);
        var exception = await action.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(expected);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? default! : (T)value!;
    }
}
