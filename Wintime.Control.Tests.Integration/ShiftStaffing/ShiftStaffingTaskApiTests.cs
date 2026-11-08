using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.ShiftStaffing;

[Collection("Integration")]
public class ShiftStaffingTaskApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static int _dateOffset = 3000;

    private readonly IntegrationTestFactory _factory;

    public ShiftStaffingTaskApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);

    [Fact]
    public async Task ShiftLead_ReadsFullTaskAndResultsFromNearestAndOwnHistory_LegacyTaskEndpointsStayForbidden()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var otherImmId = await SeedImmAsync(isActive: true);
        var archivedHistoricalImmId = await SeedImmAsync(isActive: false);
        var nearest = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-2), now.AddHours(2)))).Items.Single();
        var historical = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-6), now.AddHours(-4)))).Items.Single();
        var nearestTask = await SeedTaskAsync(otherImmId, TaskStatus.Completed,
            nearest.EndAtUtc.AddDays(2), setupStartedAt: nearest.StartAtUtc.AddMinutes(20),
            startedAt: nearest.StartAtUtc.AddMinutes(30), completedAt: nearest.StartAtUtc.AddHours(1),
            actualQuantity: 12, defectQuantity: 2);
        var historicalTask = await SeedTaskAsync(archivedHistoricalImmId, TaskStatus.Completed,
            historical.EndAtUtc.AddDays(2), setupStartedAt: historical.StartAtUtc.AddMinutes(10),
            startedAt: historical.StartAtUtc.AddMinutes(20), completedAt: historical.StartAtUtc.AddHours(1),
            actualQuantity: 8, defectQuantity: 1);
        await SeedCycleAsync(nearestTask, nearest.StartAtUtc.AddMinutes(40));
        var leadClient = await ClientAsync(lead);
        var managerClient = await ManagerClientAsync();
        var expectedTask = await managerClient.GetFromJsonAsync<TaskDto>(
            $"/api/tasks/{nearestTask.Id}", JsonOptions);
        var expectedResults = await managerClient.GetFromJsonAsync<TaskResultsDto>(
            $"/api/tasks/{nearestTask.Id}/results", JsonOptions);

        var nearestBody = await RequiredJsonAsync(await leadClient.GetAsync(TaskUrl(nearest.Id, nearestTask.Id)));
        var historicalBody = await RequiredJsonAsync(await leadClient.GetAsync(
            TaskUrl(historical.Id, historicalTask.Id)));

        nearestBody.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("task", "results");
        nearestBody.GetProperty("task").Deserialize<TaskDto>(JsonOptions).Should().BeEquivalentTo(expectedTask);
        nearestBody.GetProperty("results").Deserialize<TaskResultsDto>(JsonOptions).Should()
            .BeEquivalentTo(expectedResults);
        nearestBody.GetProperty("task").GetProperty("immId").GetGuid().Should().Be(otherImmId);
        historicalBody.GetProperty("task").GetProperty("id").GetGuid().Should().Be(historicalTask.Id);
        historicalBody.GetProperty("task").GetProperty("immId").GetGuid().Should().Be(archivedHistoricalImmId);
        historicalBody.GetProperty("results").GetProperty("output").GetProperty("actualQuantity")
            .GetInt32().Should().Be(8);

        (await leadClient.GetAsync($"/api/tasks/{nearestTask.Id}")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await leadClient.GetAsync($"/api/tasks/{nearestTask.Id}/results")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Membership_PlannedAndIssuedUsePlannedStart_OtherStatusesUseActivity()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-3);
        var end = now.AddHours(1);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var plannedInside = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(10));
        var plannedOutside = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddSeconds(-1));
        var issuedInside = await SeedTaskAsync(immId, TaskStatus.Issued, start.AddMinutes(20));
        var issuedAtEnd = await SeedTaskAsync(immId, TaskStatus.Issued, end);
        var setup = await SeedTaskAsync(immId, TaskStatus.Setup, end.AddDays(2),
            setupStartedAt: start.AddMinutes(30));
        var inProgress = await SeedTaskAsync(immId, TaskStatus.InProgress, end.AddDays(2),
            setupStartedAt: start.AddMinutes(35), startedAt: start.AddMinutes(40));
        var completedInside = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(2),
            setupStartedAt: start.AddMinutes(45), startedAt: start.AddMinutes(50),
            completedAt: start.AddHours(1));
        var completedOutside = await SeedTaskAsync(immId, TaskStatus.Completed, start.AddMinutes(15),
            setupStartedAt: start.AddHours(-3), startedAt: start.AddHours(-2),
            completedAt: start.AddHours(-1));
        var client = await ClientAsync(lead);

        foreach (var task in new[] { plannedInside, issuedInside, setup, inProgress, completedInside })
        {
            var body = await RequiredJsonAsync(await client.GetAsync(TaskUrl(item.Id, task.Id)));
            body.GetProperty("task").GetProperty("id").GetGuid().Should().Be(task.Id);
        }

        foreach (var task in new[] { plannedOutside, issuedAtEnd, completedOutside })
        {
            (await client.GetAsync(TaskUrl(item.Id, task.Id))).StatusCode.Should()
                .Be(HttpStatusCode.NotFound, task.Id.ToString());
        }
    }

    [Fact]
    public async Task Access_ForeignAndNonNearestItemsReturn403_MissingAndOutOfWindowTasksReturn404()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var otherLead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync(isActive: true);
        var nearestDay = await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)),
            Item(2, lead.Id, now.AddHours(-1), now.AddHours(1)));
        var nearest = nearestDay.Items.Single(i => i.Position == 1);
        var nonNearest = nearestDay.Items.Single(i => i.Position == 2);
        var later = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(2), now.AddHours(3)))).Items.Single();
        var foreign = (await SeedDayAsync(
            Item(1, otherLead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var historical = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-6), now.AddHours(-4)))).Items.Single();
        var nonNearestTask = await SeedTaskAsync(immId, TaskStatus.Planned, nonNearest.StartAtUtc.AddMinutes(10));
        var laterTask = await SeedTaskAsync(immId, TaskStatus.Planned, later.StartAtUtc.AddMinutes(10));
        var foreignTask = await SeedTaskAsync(immId, TaskStatus.Planned, foreign.StartAtUtc.AddMinutes(10));
        var historicalTask = await SeedTaskAsync(immId, TaskStatus.Completed, historical.StartAtUtc,
            setupStartedAt: historical.StartAtUtc, startedAt: historical.StartAtUtc.AddMinutes(10),
            completedAt: historical.StartAtUtc.AddHours(1));
        var client = await ClientAsync(lead);

        (await client.GetAsync(TaskUrl(nonNearest.Id, nonNearestTask.Id))).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(TaskUrl(later.Id, laterTask.Id))).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(TaskUrl(foreign.Id, foreignTask.Id))).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(TaskUrl(Guid.NewGuid(), foreignTask.Id))).StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
        (await client.GetAsync(TaskUrl(nearest.Id, Guid.NewGuid()))).StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
        (await client.GetAsync(TaskUrl(nearest.Id, historicalTask.Id))).StatusCode.Should()
            .Be(HttpStatusCode.NotFound, "задание принадлежит окну другой смены");
    }

    [Fact]
    public async Task ManagerAndAdmin_ReadTaskFromAnyHistoricalItem()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync(isActive: true);
        var historical = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-6), now.AddHours(-4)))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, historical.StartAtUtc,
            setupStartedAt: historical.StartAtUtc, startedAt: historical.StartAtUtc.AddMinutes(10),
            completedAt: historical.StartAtUtc.AddHours(1), actualQuantity: 10);

        foreach (var client in new[] { await ManagerClientAsync(), await AdminClientAsync() })
        {
            var body = await RequiredJsonAsync(await client.GetAsync(TaskUrl(historical.Id, task.Id)));
            body.GetProperty("task").GetProperty("id").GetGuid().Should().Be(task.Id);
            body.GetProperty("results").GetProperty("output").GetProperty("actualQuantity")
                .GetInt32().Should().Be(10);
        }
    }

    private static string TaskUrl(Guid itemId, Guid taskId) =>
        $"/api/shift-staffing/items/{itemId}/tasks/{taskId}";

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user.Login, user.Password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerClientAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdminClientAsync() => ClientAsync("admin", "Admin123!");

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"staffing_task_{role}_{suffix}";
        const string password = "Passw0rd!";
        var user = new User
        {
            UserName = login,
            NormalizedUserName = login.ToUpperInvariant(),
            Email = $"{login}@test.local",
            NormalizedEmail = $"{login.ToUpperInvariant()}@TEST.LOCAL",
            FullName = $"{role} {suffix}",
            EmployeeId = $"EMP-{suffix}"[..12],
            Role = role,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password);
    }

    private async Task<Guid> SeedImmAsync(bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"STAFFING-TASK-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = isActive
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(int position, string leadId, DateTime start, DateTime end) => new()
    {
        Position = position,
        ShiftLeadId = leadId,
        StartAtUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
        EndAtUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc),
        TimeZoneId = "Europe/Moscow"
    };

    private async Task<ShiftPlanDay> SeedDayAsync(params ShiftPlanItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay
        {
            ShiftDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset))
        };
        foreach (var item in items) day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return day;
    }

    private async Task<ShiftTask> SeedTaskAsync(
        Guid immId,
        TaskStatus status,
        DateTime plannedStartAt,
        DateTime? setupStartedAt = null,
        DateTime? startedAt = null,
        DateTime? completedAt = null,
        int actualQuantity = 0,
        int defectQuantity = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var local = schedule.ToLocal(DateTime.SpecifyKind(plannedStartAt, DateTimeKind.Utc));
        var task = new ShiftTask
        {
            ImmId = immId,
            MoldId = _factory.TestMoldId,
            ProductId = _factory.TestProductId,
            PlanQuantity = 10,
            ActualQuantity = actualQuantity,
            DefectQuantity = defectQuantity,
            Cavities = 1,
            PartWeightGrams = 5,
            Status = status,
            PlannedDate = DateOnly.FromDateTime(local),
            PlannedSetupStartTime = TimeOnly.FromDateTime(local),
            PlannedSetupMinutes = 15,
            PlannedFullCycleSeconds = 60,
            SetupStartedAt = Utc(setupStartedAt),
            StartedAt = Utc(startedAt),
            CompletedAt = Utc(completedAt),
            IssuedAt = status == TaskStatus.Issued ? DateTime.UtcNow : null
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private async Task SeedCycleAsync(ShiftTask task, DateTime start)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = task.ImmId,
            TaskId = task.Id,
            StartTime = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndTime = DateTime.SpecifyKind(start.AddSeconds(30), DateTimeKind.Utc),
            DurationSeconds = 30,
            IsSuccessful = true,
            Cavities = 1
        });
        await db.SaveChangesAsync();
    }

    private static DateTime? Utc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }
}
