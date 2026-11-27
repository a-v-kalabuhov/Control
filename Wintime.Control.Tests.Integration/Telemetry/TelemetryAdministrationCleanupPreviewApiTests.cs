using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Xunit;

namespace Wintime.Control.Tests.Integration.TelemetryAdministration;

public sealed partial class TelemetryAdministrationApiTests
{
    [Fact]
    public async Task CleanupPreview_ReturnsExactCountAndDoesNotDelete()
    {
        var client = await AdminClientAsync();
        var date = new DateOnly(2000, 1, 2);
        var before = await PreviewAsync(client, date);
        var cutoff = new DateTime(2000, 1, 1, 21, 0, 0, DateTimeKind.Utc);
        var immId = await SeedTelemetryAsync(cutoff.AddTicks(-1), cutoff, cutoff.AddTicks(1));
        try
        {
            var result = await PreviewAsync(client, date);

            result.Date.Should().Be(date);
            result.CutoffUtc.Should().Be(cutoff);
            result.CutoffUtc.Kind.Should().Be(DateTimeKind.Utc);
            result.Count.Should().Be(before.Count + 1);
            (await TelemetryCountAsync(immId)).Should().Be(3);
        }
        finally
        {
            await DeleteTelemetryAsync(immId);
        }
    }

    [Fact]
    public async Task CleanupPreview_ZeroCountAndInvalidInputs_AreHandled()
    {
        var client = await AdminClientAsync();

        var zero = await PreviewAsync(client, new DateOnly(2000, 1, 1));
        zero.Count.Should().Be(0);
        (await client.GetAsync("/api/admin/telemetry/cleanup/preview?date=not-a-date&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Moscow))).AddDays(1);
        (await client.GetAsync($"/api/admin/telemetry/cleanup/preview?date={tomorrow:yyyy-MM-dd}&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/admin/telemetry/cleanup/preview?date=2000-01-01&timeZone=Not%2FA_Real_Zone"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/admin/telemetry/cleanup/preview?date=2000-01-01"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CleanupPreview_SkippedDstMidnight_ReturnsFirstValidInstant()
    {
        const string zoneId = "America/Havana";
        var client = await AdminClientAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var date = new DateOnly(2025, 3, 9);
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var firstValidLocal = localMidnight.AddHours(1);
        zone.IsInvalidTime(localMidnight).Should().BeTrue();
        zone.IsInvalidTime(firstValidLocal.AddTicks(-1)).Should().BeTrue();
        zone.IsInvalidTime(firstValidLocal).Should().BeFalse();
        var expectedUtc = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(firstValidLocal, zone), DateTimeKind.Utc);

        var result = await PreviewAsync(client, date, zoneId);

        result.CutoffUtc.Should().Be(expectedUtc);
    }

    [Fact]
    public async Task CleanupPreview_AmbiguousDstMidnight_ReturnsEarlierUtcInstant()
    {
        const string zoneId = "America/Havana";
        var client = await AdminClientAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var date = new DateOnly(2025, 11, 2);
        var localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        zone.IsAmbiguousTime(localMidnight).Should().BeTrue();
        var expectedUtc = zone.GetAmbiguousTimeOffsets(localMidnight)
            .Select(offset => DateTime.SpecifyKind(localMidnight - offset, DateTimeKind.Utc))
            .Min();

        var result = await PreviewAsync(client, date, zoneId);

        result.CutoffUtc.Should().Be(expectedUtc);
    }

    [Fact]
    public async Task CleanupPreview_ExtendsDatabaseTimeoutOnlyForDurationOfQuery()
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

        await service.PreviewCleanupAsync(new DateOnly(2000, 1, 1), Moscow, CancellationToken.None);

        interceptor.Values.Should().ContainSingle().Which.Should().Be(30 * 60);
        db.Database.GetCommandTimeout().Should().Be(17);
    }

    private static async Task<TelemetryCleanupPreviewDto> PreviewAsync(
        HttpClient client, DateOnly date, string timeZone = Moscow)
    {
        var response = await client.GetAsync(
            $"/api/admin/telemetry/cleanup/preview?date={date:yyyy-MM-dd}&timeZone={Uri.EscapeDataString(timeZone)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TelemetryCleanupPreviewDto>())!;
    }

    private sealed class CommandTimeoutInterceptor : DbCommandInterceptor
    {
        public List<int> Values { get; } = [];

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Values.Add(command.CommandTimeout);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Values.Add(command.CommandTimeout);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
