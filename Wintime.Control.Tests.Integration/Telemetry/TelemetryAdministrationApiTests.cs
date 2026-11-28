using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.TelemetryAdministration;

[Collection("Integration")]
public sealed partial class TelemetryAdministrationApiTests : IClassFixture<IntegrationTestFactory>
{
    private const string Moscow = "Europe/Moscow";
    private readonly IntegrationTestFactory _factory;

    public TelemetryAdministrationApiTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetStatistics_Admin_ReturnsExactCountsBoundariesAndSizes()
    {
        var client = await AdminClientAsync();
        var baseline = await GetStatisticsAsync(client);
        var now = DateTime.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Moscow);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, zone));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var currentMonday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var timestamps = new List<DateTime> { now.AddSeconds(-1), now.AddDays(1) };

        void AddPeriodBoundaries(DateOnly start, DateOnly endExclusive)
        {
            timestamps.Add(LocalStartUtc(start, zone));
            timestamps.Add(LocalStartUtc(endExclusive, zone).AddTicks(-1));
            timestamps.Add(LocalStartUtc(endExclusive, zone));
        }

        AddPeriodBoundaries(monthStart, monthStart.AddMonths(1));
        AddPeriodBoundaries(currentMonday, currentMonday.AddDays(7));
        for (var age = 3; age >= 1; age--)
        {
            var start = monthStart.AddMonths(-age);
            AddPeriodBoundaries(start, start.AddMonths(1));
        }
        for (var age = 3; age >= 1; age--)
        {
            var start = currentMonday.AddDays(-7 * age);
            AddPeriodBoundaries(start, start.AddDays(7));
        }

        var immId = await SeedTelemetryAsync(timestamps.ToArray());
        try
        {
            var result = await GetStatisticsAsync(client);

            result.TotalCount.Should().Be(baseline.TotalCount + timestamps.Count);
            result.TableSizeBytes.Should().BeGreaterThanOrEqualTo(0);
            result.IndexesSizeBytes.Should().BeGreaterThanOrEqualTo(0);
            result.CurrentMonth.IsCurrent.Should().BeTrue();
            result.CurrentMonth.EndDate.Should().Be(monthStart.AddMonths(1).AddDays(-1));
            result.RecentWeeks.Should().HaveCount(4);
            result.RecentWeeks.Select(w => w.IsCurrent).Should().Equal(false, false, false, true);
            result.PreviousMonths.Should().HaveCount(3);
            result.PreviousMonths.Should().OnlyContain(m => !m.IsCurrent);

            AssertPeriodDelta(baseline.CurrentMonth, result.CurrentMonth, timestamps, now, zone);
            for (var i = 0; i < 4; i++)
                AssertPeriodDelta(baseline.RecentWeeks[i], result.RecentWeeks[i], timestamps, now, zone);
            for (var i = 0; i < 3; i++)
                AssertPeriodDelta(baseline.PreviousMonths[i], result.PreviousMonths[i], timestamps, now, zone);
        }
        finally
        {
            await DeleteTelemetryAsync(immId);
        }
    }

    [Fact]
    public async Task GetStatistics_ExtendsDatabaseTimeoutOnlyForDurationOfQueries()
    {
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = context.Database.GetConnectionString()!;
        }

        var interceptor = new CommandTimeoutInterceptor();
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new ControlDbContext(options);
        db.Database.SetCommandTimeout(17);
        var service = new TelemetryAdministrationService(
            db, NullLogger<TelemetryAdministrationService>.Instance);

        await service.GetStatisticsAsync(Moscow, CancellationToken.None);

        interceptor.Values.Should().HaveCount(2).And.OnlyContain(timeout => timeout == 30 * 60);
        db.Database.GetCommandTimeout().Should().Be(17);
    }

    [Theory]
    [InlineData("/api/admin/telemetry/statistics")]
    [InlineData("/api/admin/telemetry/statistics?timeZone=Not%2FA_Real_Zone")]
    public async Task GetStatistics_MissingOrUnknownTimeZone_Returns400(string url)
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "admin", "Admin123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static async Task<TelemetryStatisticsDto> GetStatisticsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/telemetry/statistics?timeZone=Europe%2FMoscow");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TelemetryStatisticsDto>())!;
    }

    private async Task<Guid> SeedTelemetryAsync(params DateTime[] timestamps)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await AddTelemetryAsync(immId, timestamps);
        return immId;
    }

    private async Task AddTelemetryAsync(Guid immId, params DateTime[] timestamps)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.Telemetry.AddRange(timestamps.Select(timestamp => new Core.Entities.Telemetry
        {
            ImmId = immId,
            Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
            ParameterName = "temp",
            ValueNumeric = 1m
        }));
        await db.SaveChangesAsync();
    }

    private async Task<int> TelemetryCountAsync(Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Telemetry.Where(t => t.ImmId == immId).CountAsync();
    }

    private async Task DeleteTelemetryAsync(Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Telemetry.Where(t => t.ImmId == immId).ExecuteDeleteAsync();
    }

    private static DateTime LocalStartUtc(DateOnly date, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), DateTimeKind.Utc);

    private static void AssertPeriodDelta(
        PeriodCountDto baseline, PeriodCountDto actual, IEnumerable<DateTime> timestamps,
        DateTime nowUtc, TimeZoneInfo zone)
    {
        actual.StartDate.Should().Be(baseline.StartDate);
        actual.EndDate.Should().Be(baseline.EndDate);
        var startUtc = LocalStartUtc(actual.StartDate, zone);
        var endUtc = actual.IsCurrent ? nowUtc : LocalStartUtc(actual.EndDate.AddDays(1), zone);
        var expectedDelta = timestamps.Count(t => t >= startUtc && t < endUtc);
        actual.Count.Should().Be(baseline.Count + expectedDelta, actual.StartDate.ToString());
    }
}
