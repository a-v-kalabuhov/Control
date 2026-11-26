using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Migrations;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

public class MigrationRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task EmptyDatabase_AppliesAllMigrationsFromCurrentAssembly()
    {
        var result = await RunAsync(_postgres.GetConnectionString());

        result.Should().Be(0);
        await using var db = NewContext(_postgres.GetConnectionString());
        var expected = db.Database.GetMigrations().OrderBy(id => id).ToArray();
        var applied = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());
        applied.Should().Equal(expected);
    }

    [Fact]
    public async Task SecondRun_SucceedsWithoutChangingMigrationHistory()
    {
        (await RunAsync(_postgres.GetConnectionString())).Should().Be(0);
        var before = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());

        var result = await RunAsync(_postgres.GetConnectionString());

        result.Should().Be(0);
        var after = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());
        after.Should().Equal(before);
    }

    [Fact]
    public async Task MissingConnectionString_ReturnsNonZero()
    {
        var configuration = new ConfigurationBuilder().Build();

        var result = await MigrationRunner.RunAsync(configuration, NullLogger.Instance);

        result.Should().NotBe(0);
    }

    [Fact]
    public async Task UnreachableDatabase_ReturnsNonZeroWithoutLongWait()
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Host = "127.0.0.1",
            Port = 1,
            Timeout = 1,
            CommandTimeout = 1,
            Pooling = false
        };

        var result = await RunAsync(builder.ConnectionString);

        result.Should().NotBe(0);
    }

    private static Task<int> RunAsync(string connectionString) =>
        MigrationRunner.RunAsync(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString
                })
                .Build(),
            NullLogger.Instance);

    private static ControlDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }
}
