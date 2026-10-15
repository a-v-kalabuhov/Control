using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskResultsApiTests : IClassFixture<IntegrationTestFactory>
{
    // Завершённое задание в прошлом: окно закрыто, расчёт детерминирован.
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime M(int min) => T0.AddMinutes(min);

    private readonly IntegrationTestFactory _factory;
    public TaskResultsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string user = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    /// <summary>Свой ТПА и ПФ на тест — чтобы статусы, циклы и простои не пересекались между тестами.</summary>
    private async Task<(Guid ImmId, Guid MoldId)> SeedImmAndMoldAsync(ControlDbContext db)
    {
        var imm = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var mold = new Mold { Name = "M-results", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true };
        db.Imms.Add(imm);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (imm.Id, mold.Id);
    }

    private static ImmCycle Cycle(Guid immId, Guid? taskId, DateTime start, int durationS,
        int? injectionMs, int? pauseMs, bool successful = true, bool closed = true) => new()
    {
        ImmId = immId, TaskId = taskId, StartTime = start,
        EndTime = closed ? start.AddSeconds(durationS) : null,
        DurationSeconds = closed ? durationS : 0, IsSuccessful = successful, Cavities = 2,
        InjectionDurationMs = injectionMs, PauseDurationMs = pauseMs
    };

    private static Event Downtime(Guid immId, DateTime start, DateTime? end) => new()
    {
        ImmId = immId, EventType = EventType.Downtime, StartTime = start, EndTime = end, IsAuto = true
    };

    private static ImmStatusHistory Raw(Guid immId, string status, DateTime from, DateTime to) =>
        new() { ImmId = immId, Status = status, ChangedAt = from, EndedAt = to };

    [Fact]
    public async Task UnknownTask_Returns404()
    {
        var client = await ClientAsync();

        var resp = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}/results");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Observer_Returns403()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask { ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = TaskStatus.Planned };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync("test_observer", "Observer123!");

        var resp = await client.GetAsync($"/api/tasks/{taskId}/results");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReleaseNotStarted_ReturnsFlagAndNullBlocks()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = TaskStatus.Setup,
                SetupStartedAt = M(0)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.ReleaseStarted.Should().BeFalse();
        result.InProgress.Should().BeFalse();
        result.ReleaseFrom.Should().BeNull();
        result.ReleaseTo.Should().BeNull();
        result.Output.Should().BeNull();
        result.Cycles.Should().BeNull();
        result.Time.Should().BeNull();
    }

    [Fact]
    public async Task CompletedTask_ComputesOutputCyclesAndTimeOverReleaseWindow()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed,
                PlanQuantity = 100, ActualQuantity = 110, DefectQuantity = 4,
                CloseReason = "Перевыпуск по просьбе мастера",
                PlannedFullCycleSeconds = 60, PlannedInjectionCycleSeconds = 55,
                SetupStartedAt = M(-30), StartedAt = M(0), CompletedAt = M(60)
            };
            var other = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 1,
                SetupStartedAt = M(-200), StartedAt = M(-190), CompletedAt = M(-100)
            };
            db.ShiftTasks.AddRange(task, other);
            await db.SaveChangesAsync();

            // Окно выпуска 0–60 мин. Сырой: idle 0–5, auto 5–10, offline 10–30, idle 30–50, auto 50–60.
            db.ImmStatusHistory.AddRange(
                Raw(immId, ImmStatus.Idle,    M(0),  M(5)),
                Raw(immId, ImmStatus.Auto,    M(5),  M(10)),
                Raw(immId, ImmStatus.Offline, M(10), M(30)),
                Raw(immId, ImmStatus.Idle,    M(30), M(50)),
                Raw(immId, ImmStatus.Auto,    M(50), M(60)));
            // Простой, начавшийся до окна (обрезается по окну), простой 35–50 и простой после окна (не считается).
            db.Events.AddRange(
                Downtime(immId, M(-20), M(5)),
                Downtime(immId, M(35),  M(50)),
                Downtime(immId, M(70),  M(80)));
            // Закрытые успешные циклы задания: длит. 10/12/14 → 12; пауза 20/null/30 с → 25; литьё 8/9/10 → 9.
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(51), 10, 8000,  20000),
                Cycle(immId, task.Id, M(52), 12, 9000,  null),
                Cycle(immId, task.Id, M(53), 14, 10000, 30000),
                Cycle(immId, task.Id, M(54), 99, 99000, 99000, closed: false),     // открытый
                Cycle(immId, task.Id, M(55), 99, 99000, 99000, successful: false), // неуспешный
                Cycle(immId, other.Id, M(-150), 99, 99000, 99000));                // чужое задание
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.ReleaseStarted.Should().BeTrue();
        result.InProgress.Should().BeFalse();
        result.ReleaseFrom.Should().Be(M(0));
        result.ReleaseTo.Should().Be(M(60));

        result.Output!.PlanQuantity.Should().Be(100);
        result.Output.ActualQuantity.Should().Be(110);
        result.Output.DefectQuantity.Should().Be(4);
        result.Output.GoodQuantity.Should().Be(106);
        result.Output.OverrunQuantity.Should().Be(10);
        result.Output.DeviationReason.Should().Be("Перевыпуск по просьбе мастера");

        result.Cycles!.Count.Should().Be(3);
        result.Cycles.AvgInjectionSeconds.Should().BeApproximately(9.0, 1e-6);
        result.Cycles.AvgFullCycleSeconds.Should().BeApproximately(12.0 + 25.0, 1e-6);
        result.Cycles.PlannedInjectionSeconds.Should().Be(55);
        result.Cycles.PlannedFullCycleSeconds.Should().Be(60);

        // 0–5 простой (событие) · 5–10 работа · 10–30 нет связи · 30–35 работа (до порога) ·
        // 35–50 простой · 50–60 работа → по 20 мин каждого.
        result.Time!.WindowSeconds.Should().Be(3600);
        result.Time.ProductionSeconds.Should().Be(1200);
        result.Time.DowntimeSeconds.Should().Be(1200);
        result.Time.OfflineSeconds.Should().Be(1200);
        result.Time.DowntimeCount.Should().Be(2);
    }

    [Fact]
    public async Task FewerThanThreeClosedCycles_AveragesAreNull()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 10,
                PlannedFullCycleSeconds = 60, SetupStartedAt = M(-10), StartedAt = M(0), CompletedAt = M(10)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(1), 10, 8000, 20000),
                Cycle(immId, task.Id, M(2), 12, 9000, 20000));
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.Cycles!.Count.Should().Be(2);
        result.Cycles.AvgInjectionSeconds.Should().BeNull();
        result.Cycles.AvgFullCycleSeconds.Should().BeNull();
        result.Cycles.PlannedFullCycleSeconds.Should().Be(60);
        result.Cycles.PlannedInjectionSeconds.Should().BeNull();
    }

    [Fact]
    public async Task CyclesWithoutInjectionDuration_InjectionAverageIsNull()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Completed, PlanQuantity = 10,
                SetupStartedAt = M(-10), StartedAt = M(0), CompletedAt = M(10)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            db.ImmCycles.AddRange(
                Cycle(immId, task.Id, M(1), 10, null, null),
                Cycle(immId, task.Id, M(2), 10, null, null),
                Cycle(immId, task.Id, M(3), 10, null, null));
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.Cycles!.Count.Should().Be(3);
        result.Cycles.AvgInjectionSeconds.Should().BeNull();
        result.Cycles.AvgFullCycleSeconds.Should().BeApproximately(10.0, 1e-6); // пауз нет → только длительность
    }

    [Fact]
    public async Task InProgressTask_WindowRunsToNow_NoUnderrunReasonAndNoOverrun()
    {
        Guid taskId;
        var startedAt = DateTime.UtcNow.AddHours(-2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.InProgress,
                PlanQuantity = 100, ActualQuantity = 40,
                SetupStartedAt = startedAt.AddMinutes(-15), StartedAt = startedAt
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.InProgress.Should().BeTrue();
        result.ReleaseTo.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        result.Output!.OverrunQuantity.Should().Be(0);
        result.Output.GoodQuantity.Should().Be(40);
        result.Output.DeviationReason.Should().BeNull();
        // Сырой истории нет → всё окно «Нет связи» (gapAsNoData: false).
        result.Time!.OfflineSeconds.Should().BeCloseTo(result.Time.WindowSeconds, 1);
    }

    [Fact]
    public async Task ClosedWithoutCompletion_WindowEndsAtClosedAt()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var (immId, moldId) = await SeedImmAndMoldAsync(db);
            var task = new ShiftTask
            {
                ImmId = immId, MoldId = moldId, Status = TaskStatus.Closed, PlanQuantity = 10,
                CloseReason = "Сломалась ПФ",
                SetupStartedAt = M(-10), StartedAt = M(0), ClosedAt = M(30)
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await ClientAsync();

        var result = (await client.GetFromJsonAsync<TaskResultsDto>($"/api/tasks/{taskId}/results"))!;

        result.InProgress.Should().BeFalse();
        result.ReleaseTo.Should().Be(M(30));
        result.Time!.WindowSeconds.Should().Be(1800);
        // Причина отклонения — только для Completed; у Closed это причина закрытия (вкладка «Данные задания»).
        result.Output!.DeviationReason.Should().BeNull();
    }
}
