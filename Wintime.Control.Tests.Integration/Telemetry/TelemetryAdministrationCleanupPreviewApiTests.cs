using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Telemetry;
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

    private static async Task<TelemetryCleanupPreviewDto> PreviewAsync(HttpClient client, DateOnly date)
    {
        var response = await client.GetAsync(
            $"/api/admin/telemetry/cleanup/preview?date={date:yyyy-MM-dd}&timeZone=Europe%2FMoscow");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TelemetryCleanupPreviewDto>())!;
    }
}
