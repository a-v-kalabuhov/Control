using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

/// <summary>BL-32: плитка ТПА — темп и прогноз окончания выпуска по среднему периоду цикла.</summary>
[Collection("Integration")]
public class ImmListReleaseForecastTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ImmListReleaseForecastTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<ImmDto> GetImmFromListAsync(Guid immId)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var list = await client.GetFromJsonAsync<List<ImmDto>>("/api/imm");
        return list!.Single(i => i.Id == immId);
    }

    /// Задание: план 100, факт 40, 2 гнезда, эталон 60 с; три закрытых цикла по 20 с,
    /// паузы 10 с, null, 10 с (среднее 10 с) и один открытый цикл.
    private async Task<Guid> SeedAsync(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = "M-BL32", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true, ProductTypeId = _factory.TestProductTypeId };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = mold.Id, ProductId = _factory.TestProductId, PlanQuantity = 100, ActualQuantity = 40,
            PlannedFullCycleSeconds = 60, Status = status, SetupStartedAt = t0.AddMinutes(-5),
            StartedAt = status == TaskStatus.InProgress ? t0 : null
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        int?[] pauses = [10_000, null, 10_000];
        for (var i = 0; i < 3; i++)
        {
            var start = t0.AddSeconds(i * 30);
            db.ImmCycles.Add(new ImmCycle
            {
                ImmId = immId, TaskId = task.Id, MoldId = mold.Id, StartTime = start, EndTime = start.AddSeconds(20),
                DurationSeconds = 20, PauseDurationMs = pauses[i], Cavities = 2, IsSuccessful = true
            });
        }
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = immId, TaskId = task.Id, MoldId = mold.Id, StartTime = t0.AddSeconds(90), EndTime = null,
            PauseDurationMs = 50_000, Cavities = 2, IsSuccessful = true // открытый — в среднее не входит
        });
        await db.SaveChangesAsync();
        return immId;
    }

    [Fact]
    public async Task InProgress_task_gets_rate_and_forecast_by_mean_cycle_period()
    {
        var immId = await SeedAsync(TaskStatus.InProgress);
        var before = DateTime.UtcNow;

        var dto = await GetImmFromListAsync(immId);

        // период 20 + 10 = 30 с → 2 × 3600 / 30 = 240 шт/ч; остаток 60 шт = 30 смыканий × 30 с = 900 с
        dto.ReleaseRatePerHour.Should().Be(240m);
        dto.ReleaseForecastAt.Should().NotBeNull();
        dto.ReleaseForecastAt!.Value.Should().BeCloseTo(before.AddSeconds(900), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Setup_task_has_no_forecast()
    {
        var immId = await SeedAsync(TaskStatus.Setup);

        var dto = await GetImmFromListAsync(immId);

        dto.ReleaseRatePerHour.Should().BeNull();
        dto.ReleaseForecastAt.Should().BeNull();
    }
}
