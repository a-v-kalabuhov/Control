using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

/// <summary>
/// ARCH-03: отчёт «Рабочий день» для заданий через несколько суток и нескольких заданий за сутки.
/// Метки времени выбраны так, чтобы попадать в те же локальные сутки завода при поясе от UTC−6 до UTC+10
/// (по умолчанию — Europe/Moscow).
/// </summary>
[Collection("Integration")]
public class DailyReportMultiDayTaskTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public DailyReportMultiDayTaskTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<DailyReportImmItemDto> GetItemAsync(DateTime day, Guid immId)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/reports/daily?date={day:yyyy-MM-dd}&immId={immId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await resp.Content.ReadFromJsonAsync<DailyReportDto>();
        return report!.ImmData.Should().ContainSingle().Subject;
    }

    private static Mold NewMold(string name, int cavities, decimal part, decimal runner) => new()
    {
        Name = name, FormId = $"F-{Guid.NewGuid():N}", Cavities = cavities,
        PartWeightGrams = part, RunnerWeightGrams = runner, IsActive = true
    };

    [Fact]
    public async Task ThreeDayTask_SecondDayShowsMoldAndNormativePlan_SumEqualsTotalNorm()
    {
        var d1 = new DateTime(2025, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var immId = await _factory.CreateFreshImmAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = NewMold($"ПФ-3сут-{Guid.NewGuid():N}", 2, 10, 4);
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            // Выпуск: d1 11:00Z → d3 11:00Z (48 ч), норма 60 с × 2 гнезда = 120 шт/ч
            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = immId, MoldId = mold.Id, PlanQuantity = 100_000, PlannedFullCycleSeconds = 60,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d1),
                SetupStartedAt = d1.AddHours(10), StartedAt = d1.AddHours(11), CompletedAt = d1.AddDays(2).AddHours(11)
            });
            await db.SaveChangesAsync();
        }

        var day1 = await GetItemAsync(d1, immId);
        var day2 = await GetItemAsync(d1.AddDays(1), immId);
        var day3 = await GetItemAsync(d1.AddDays(2), immId);

        day2.MoldName.Should().StartWith("ПФ-3сут-");
        day2.PlanQuantity.Should().Be(2880);
        (day1.PlanQuantity + day2.PlanQuantity + day3.PlanQuantity).Should().Be(5760);
    }

    [Fact]
    public async Task TwoTasksOneDay_MoldsListed_PlansSummed_RawMaterialPerCycleMold()
    {
        var d = new DateTime(2025, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var immId = await _factory.CreateFreshImmAsync();
        string mold1Name, mold2Name;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold1 = NewMold($"ПФ-A-{Guid.NewGuid():N}", 2, 10, 4);
            var mold2 = NewMold($"ПФ-B-{Guid.NewGuid():N}", 1, 5, 1);
            db.Molds.AddRange(mold1, mold2);
            await db.SaveChangesAsync();
            mold1Name = mold1.Name; mold2Name = mold2.Name;

            // Задание 1: выпуск 08:00–10:00, норма 120 шт/ч → 240, план 100 → 100
            var t1 = new ShiftTask
            {
                ImmId = immId, MoldId = mold1.Id, PlanQuantity = 100, PlannedFullCycleSeconds = 60,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d),
                SetupStartedAt = d.AddHours(7), StartedAt = d.AddHours(8), CompletedAt = d.AddHours(10)
            };
            // Задание 2: выпуск 12:00–13:00, норма 30 с × 1 гнездо → 120
            var t2 = new ShiftTask
            {
                ImmId = immId, MoldId = mold2.Id, PlanQuantity = 1000, PlannedFullCycleSeconds = 30,
                Status = TaskStatus.Completed, PlannedDate = DateOnly.FromDateTime(d),
                SetupStartedAt = d.AddHours(11), StartedAt = d.AddHours(12), CompletedAt = d.AddHours(13)
            };
            db.ShiftTasks.AddRange(t1, t2);
            await db.SaveChangesAsync();

            ImmCycle Cycle(Guid moldId, Guid taskId, int cavities, DateTime start) => new()
            {
                ImmId = immId, MoldId = moldId, TaskId = taskId, Cavities = cavities, IsSuccessful = true,
                StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60
            };
            db.ImmCycles.AddRange(
                Cycle(mold1.Id, t1.Id, 2, d.AddHours(9)),
                Cycle(mold1.Id, t1.Id, 2, d.AddHours(9).AddMinutes(1)),
                Cycle(mold2.Id, t2.Id, 1, d.AddHours(12).AddMinutes(30)));
            await db.SaveChangesAsync();
        }

        var item = await GetItemAsync(d, immId);

        item.MoldName.Should().Be($"{mold1Name}, {mold2Name}");
        item.PlanQuantity.Should().Be(100 + 120);
        item.ActualQuantity.Should().Be(5);
        // ПФ-A: 2 цикла × (10 г × 2 гнезда + 4 г литник) = 48 г; ПФ-B: 1 × (5 × 1 + 1) = 6 г
        item.RawMaterialKg.Should().Be(0.054m);
    }
}
