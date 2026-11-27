using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.TelemetryAdministration;

public sealed partial class TelemetryAdministrationApiTests
{
    [Fact]
    public async Task Cleanup_DeletesOnlyRowsBeforeCutoffAndPreservesOtherTables()
    {
        var client = await AdminClientAsync();
        var date = new DateOnly(2000, 1, 2);
        var cutoff = new DateTime(2000, 1, 1, 21, 0, 0, DateTimeKind.Utc);
        var baseline = await PreviewAsync(client, date);
        var immId = await SeedTelemetryAsync(cutoff.AddTicks(-1), cutoff, cutoff.AddTicks(1));
        await SeedRelatedRowsAsync(immId, cutoff);
        try
        {
            var response = await client.DeleteAsync(
                $"/api/admin/telemetry/cleanup?date={date:yyyy-MM-dd}&timeZone=Europe%2FMoscow");

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var result = (await response.Content.ReadFromJsonAsync<TelemetryCleanupResultDto>())!;
            result.CutoffUtc.Should().Be(cutoff);
            result.DeletedCount.Should().Be(baseline.Count + 1);
            (await TelemetryCountAsync(immId)).Should().Be(2);
            (await RelatedCountsAsync(immId)).Should().Be((1, 1));
        }
        finally
        {
            await DeleteTelemetryAsync(immId);
            await DeleteRelatedRowsAsync(immId);
        }
    }

    [Fact]
    public async Task Cleanup_ReturnsActualDeletedCountAndRepeatedCallIsZero()
    {
        var client = await AdminClientAsync();
        var date = new DateOnly(2000, 1, 3);
        var cutoff = new DateTime(2000, 1, 2, 21, 0, 0, DateTimeKind.Utc);
        var baseline = await PreviewAsync(client, date);
        var immId = await SeedTelemetryAsync(cutoff.AddMinutes(-2));
        try
        {
            var preview = await PreviewAsync(client, date);
            preview.Count.Should().Be(baseline.Count + 1);
            await AddTelemetryAsync(immId, cutoff.AddMinutes(-1)); // опоздавшая запись после preview

            var first = await DeleteCleanupAsync(client, date);
            var second = await DeleteCleanupAsync(client, date);

            first.DeletedCount.Should().Be(preview.Count + 1);
            second.DeletedCount.Should().Be(0);
            (await TelemetryCountAsync(immId)).Should().Be(0);
        }
        finally
        {
            await DeleteTelemetryAsync(immId);
        }
    }

    [Fact]
    public async Task Cleanup_InvalidInputs_Return400()
    {
        var client = await AdminClientAsync();
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Moscow))).AddDays(1);

        (await client.DeleteAsync($"/api/admin/telemetry/cleanup?date={tomorrow:yyyy-MM-dd}&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync("/api/admin/telemetry/cleanup?date=bad&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.DeleteAsync("/api/admin/telemetry/cleanup?date=2000-01-01&timeZone=Not%2FA_Real_Zone"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Adjuster)]
    [InlineData(UserRole.Observer)]
    [InlineData(UserRole.Emulator)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.ShiftLead)]
    public async Task Endpoints_NonAdmin_ReturnForbidden(UserRole role)
    {
        var client = await ClientForRoleAsync(role);

        (await client.GetAsync("/api/admin/telemetry/statistics?timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/admin/telemetry/cleanup/preview?date=2000-01-01&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync("/api/admin/telemetry/cleanup?date=2000-01-01&timeZone=Europe%2FMoscow"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<TelemetryCleanupResultDto> DeleteCleanupAsync(HttpClient client, DateOnly date)
    {
        var response = await client.DeleteAsync(
            $"/api/admin/telemetry/cleanup?date={date:yyyy-MM-dd}&timeZone=Europe%2FMoscow");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TelemetryCleanupResultDto>())!;
    }

    private async Task<HttpClient> ClientForRoleAsync(UserRole role)
    {
        var login = $"telemetry_{role}_{Guid.NewGuid():N}";
        const string password = "Telemetry123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var created = await users.CreateAsync(new User
            {
                UserName = login, Email = $"{login}@test.local", FullName = role.ToString(),
                Role = role, IsActive = true
            }, password);
            created.Succeeded.Should().BeTrue(string.Join("; ", created.Errors.Select(e => e.Description)));
        }
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task SeedRelatedRowsAsync(Guid immId, DateTime timestamp)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = immId, StartTime = timestamp, EndTime = timestamp.AddSeconds(1),
            DurationSeconds = 1, IsSuccessful = true, Cavities = 1
        });
        db.ImmStatusHistory.Add(new ImmStatusHistory
        {
            ImmId = immId, Status = "Auto", ChangedAt = timestamp
        });
        await db.SaveChangesAsync();
    }

    private async Task<(int Cycles, int Statuses)> RelatedCountsAsync(Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return (await db.ImmCycles.CountAsync(c => c.ImmId == immId),
            await db.ImmStatusHistory.CountAsync(s => s.ImmId == immId));
    }

    private async Task DeleteRelatedRowsAsync(Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ImmCycles.Where(c => c.ImmId == immId).ExecuteDeleteAsync();
        await db.ImmStatusHistory.Where(s => s.ImmId == immId).ExecuteDeleteAsync();
    }
}
