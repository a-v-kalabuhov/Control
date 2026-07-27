using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class TelemetryDashboardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TelemetryDashboardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<Guid> SeedImmWithTelemetryAsync(DateTime from)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Core.Entities.Imm
        {
            Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();

        // Шаблон TestTemplate содержит датчик "temp" типа float.
        db.Telemetry.Add(new Telemetry { ImmId = imm.Id, Timestamp = from.AddMinutes(1), ParameterName = "temp", ValueNumeric = 218.4m });
        db.Telemetry.Add(new Telemetry { ImmId = imm.Id, Timestamp = from.AddMinutes(2), ParameterName = "temp", ValueNumeric = 219.1m });
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = imm.Id, StartTime = from.AddMinutes(1), EndTime = from.AddMinutes(2),
            DurationSeconds = 60, IsSuccessful = true, Cavities = 1
        });
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task Returns_Signals_Cycles_And_StatusSegments()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={to:O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<TelemetryDashboardDto>();
        dto.Should().NotBeNull();
        dto!.Signals.Should().ContainSingle();
        dto.Signals[0].ParameterName.Should().Be("temp");
        dto.Signals[0].Type.Should().Be("float");
        dto.Signals[0].Points.Should().HaveCount(2);
        dto.Cycles.Should().ContainSingle();
        dto.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_Parameters_Returns_400()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(1):O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Window_Over_4h_Returns_400()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(5):O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adjuster_Is_Forbidden()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={from.AddHours(1):O}&parameters=temp";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Signals_Endpoint_Returns_Template_Sensors()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        var resp = await client.GetAsync($"/api/imm/{immId}/signals");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var signals = await resp.Content.ReadFromJsonAsync<List<TelemetrySignalMetaDto>>();
        signals.Should().NotBeNull();
        signals!.Should().Contain(s => s.ParameterName == "temp" && s.Type == "float");
    }

    [Fact]
    public async Task PointsFrom_Narrows_Telemetry_But_Keeps_FullWindow_Cycles()
    {
        var from = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);
        // Точки на +1 и +2 мин, цикл на +1..+2 мин.
        var immId = await SeedImmWithTelemetryAsync(from);

        var client = await ManagerClientAsync();
        // pointsFrom на +90 c — первая точка (+1 мин) должна отсеяться, вторая (+2 мин) остаться.
        var pointsFrom = from.AddSeconds(90);
        var url = $"/api/imm/{immId}/telemetry-dashboard?from={from:O}&to={to:O}&parameters=temp&pointsFrom={pointsFrom:O}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<TelemetryDashboardDto>();
        dto!.Signals[0].Points.Should().ContainSingle("точки сужены pointsFrom");
        dto.Cycles.Should().ContainSingle("циклы всегда за полное окно, pointsFrom их не трогает");
    }
}
