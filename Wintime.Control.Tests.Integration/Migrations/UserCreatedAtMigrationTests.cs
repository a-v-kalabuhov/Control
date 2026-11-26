using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

/// <summary>Серверная дата создания пользователя и индекс выборки первого активного Manager.</summary>
public class UserCreatedAtMigrationTests : IAsyncLifetime
{
    private const string FirstUserId = "created-at-user-1";
    private const string SecondUserId = "created-at-user-2";
    private const string IndexName = "IX_Users_IsActive_Role_CreatedAt_Id";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    private ControlDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ControlDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Up_BackfillsExistingUsersWithUtcAndCreatesManagerFallbackIndex()
    {
        await using var db = NewContext();
        var (migrator, previous, target) = FindMigration(db);
        await migrator.MigrateAsync(previous);
        await ExecuteSqlAsync(UserInsertSql(FirstUserId) + UserInsertSql(SecondUserId));

        var backfillStarted = await ScalarAsync<DateTime>("SELECT now()");
        await migrator.MigrateAsync(target);
        var backfillFinished = await ScalarAsync<DateTime>("SELECT now()");

        var first = await ScalarAsync<DateTime>($"SELECT \"CreatedAt\" FROM \"Users\" WHERE \"Id\" = '{FirstUserId}'");
        var second = await ScalarAsync<DateTime>($"SELECT \"CreatedAt\" FROM \"Users\" WHERE \"Id\" = '{SecondUserId}'");
        first.Kind.Should().Be(DateTimeKind.Utc);
        second.Kind.Should().Be(DateTimeKind.Utc);
        first.Should().NotBe(default);
        second.Should().Be(first, "backfill использует единое время миграции");
        first.Should().BeOnOrAfter(backfillStarted).And.BeOnOrBefore(backfillFinished);

        (await ScalarAsync<string>(ColumnMetadataSql("data_type"))).Should().Be("timestamp with time zone");
        (await ScalarAsync<string>(ColumnMetadataSql("is_nullable"))).Should().Be("NO");
        (await ScalarAsync<string>(ColumnMetadataSql("column_default"))).Should().Be("now()");

        (await ScalarAsync<string?>($@"
SELECT string_agg(a.attname, ',' ORDER BY k.ord)
FROM pg_index i
CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum, ord)
JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum
WHERE i.indrelid = '""Users""'::regclass
  AND i.indexrelid = '""{IndexName}""'::regclass"))
            .Should().Be("IsActive,Role,CreatedAt,Id");

        var insertStarted = await ScalarAsync<DateTime>("SELECT now()");
        await ExecuteSqlAsync(UserInsertSql("created-at-user-after-up"));
        var insertFinished = await ScalarAsync<DateTime>("SELECT now()");
        var createdAfterUp = await ScalarAsync<DateTime>(
            "SELECT \"CreatedAt\" FROM \"Users\" WHERE \"Id\" = 'created-at-user-after-up'");
        createdAfterUp.Kind.Should().Be(DateTimeKind.Utc);
        createdAfterUp.Should().BeOnOrAfter(insertStarted).And.BeOnOrBefore(insertFinished);
    }

    [Fact]
    public async Task Down_DropsCreatedAtAndIndexButPreservesUsers()
    {
        await using var db = NewContext();
        var (migrator, previous, target) = FindMigration(db);
        await migrator.MigrateAsync(previous);
        await ExecuteSqlAsync(UserInsertSql(FirstUserId));
        await migrator.MigrateAsync(target);

        await migrator.MigrateAsync(previous);

        (await ScalarAsync<long>(@"
SELECT count(*) FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'Users' AND column_name = 'CreatedAt'"))
            .Should().Be(0);
        (await ScalarAsync<string?>($"SELECT to_regclass('public.\"{IndexName}\"')::text")).Should().BeNull();
        (await ScalarAsync<string>($"SELECT \"FullName\" FROM \"Users\" WHERE \"Id\" = '{FirstUserId}'"))
            .Should().Be(FirstUserId);
    }

    private static (IMigrator Migrator, string Previous, string Target) FindMigration(ControlDbContext db)
    {
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.SingleOrDefault(m => m.EndsWith("_AddUserCreatedAt"));
        target.Should().NotBeNull("миграция AddUserCreatedAt должна существовать");
        var targetIndex = all.IndexOf(target!);
        targetIndex.Should().BeGreaterThan(0);
        return (migrator, all[targetIndex - 1], target!);
    }

    private static string ColumnMetadataSql(string column) => $@"
SELECT {column} FROM information_schema.columns
WHERE table_schema = 'public' AND table_name = 'Users' AND column_name = 'CreatedAt'";

    private static string UserInsertSql(string id) => $@"
INSERT INTO ""Users""
    (""Id"",""AccessFailedCount"",""EmailConfirmed"",""FullName"",""IsActive"",""LockoutEnabled"",""PhoneNumberConfirmed"",""Role"",""TwoFactorEnabled"",""UserName"")
VALUES ('{id}',0,FALSE,'{id}',TRUE,FALSE,FALSE,1,FALSE,NULL);";

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
}
