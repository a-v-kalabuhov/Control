using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-31: пакетные индикаторы закрытия, сирот и отсутствия старшего в плане смен.</summary>
[Collection("Integration")]
public sealed class ShiftPlanClosureIndicatorTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 12000;
    private readonly IntegrationTestFactory _factory;

    public ShiftPlanClosureIndicatorTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);

    [Fact]
    public async Task Get_ComputesClosureStatusesOrphansAndOperatorEditability()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var current = Item(101, lead.Id, now.AddHours(-1), now.AddHours(1));
        var ended = Item(102, lead.Id, now.AddHours(-4), now.AddHours(-2));
        var future = Item(103, lead.Id, now.AddHours(2), now.AddHours(4));
        var closed = Item(104, lead.Id, now.AddHours(-7), now.AddHours(-5));
        var currentWithoutLead = Item(105, null, now.AddHours(-1), now.AddHours(1));
        var nearestFutureWithoutLead = Item(106, null, now.AddHours(2), now.AddHours(4));
        var day = await SeedDayAsync(date, current, ended, future, closed, currentWithoutLead,
            nearestFutureWithoutLead);
        await SeedDayAsync(NewDate(), Item(103, lead.Id, now.AddMinutes(30), now.AddMinutes(90)));
        var immId = await _factory.CreateFreshImmAsync();
        var orphanTask = await SeedTaskAsync(immId, current.StartAtUtc, now.AddMinutes(-10));
        await SeedCycleAsync(orphanTask, now.AddMinutes(-20), cavities: 3);
        await SeedTaskAsync(immId, ended.StartAtUtc, ended.EndAtUtc);
        var frozenTask = await SeedTaskAsync(immId, closed.StartAtUtc, closed.EndAtUtc);
        await SeedCycleAsync(frozenTask, closed.StartAtUtc.AddMinutes(30), cavities: 5);
        await SeedClosureAsync(closed.Id, lead.Id, ShiftClosureState.Closed);
        var client = await ManagerClientAsync();

        var body = await GetDayAsync(client, date);

        Item(body, 101).GetProperty("closureStatus").GetString().Should().Be("Draft",
            "динамический черновик не требует записи ShiftClosure");
        Item(body, 101).GetProperty("hasOrphanOutput").GetBoolean().Should().BeTrue();
        Item(body, 101).GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
        Item(body, 102).GetProperty("closureStatus").GetString().Should().Be("NotClosed");
        Item(body, 102).GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse(
            "отсутствие циклов означает нулевой автоматический выпуск, а не сироту");
        Item(body, 102).GetProperty("canEditOperators").GetBoolean().Should().BeTrue(
            "завершённая незакрытая смена доступна для ретро-привязки");
        Item(body, 103).GetProperty("closureStatus").GetString().Should().Be("Draft");
        Item(body, 103).GetProperty("canEditOperators").GetBoolean().Should().BeFalse(
            "поздняя будущая позиция не является ближайшей");
        Item(body, 104).GetProperty("closureStatus").GetString().Should().Be("Closed");
        Item(body, 104).GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse(
            "закрытый снимок не пересчитывается по появившимся циклам");
        Item(body, 104).GetProperty("canEditOperators").GetBoolean().Should().BeFalse();
        Item(body, 105).GetProperty("closureStatus").ValueKind.Should().Be(JsonValueKind.Null);
        Item(body, 105).GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeTrue();
        Item(body, 106).GetProperty("closureStatus").ValueKind.Should().Be(JsonValueKind.Null);
        Item(body, 106).GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeTrue();
        day.Items.Should().HaveCount(6);
    }

    [Fact]
    public async Task MissingLeadWarning_UsesVirtualCurrentOutsideRangeAndSkipsLateFutureAndPast()
    {
        var manager = await ManagerClientAsync();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        var schedule = new List<ShiftScheduleItemDto>
        {
            new()
            {
                StartMinutes = ((localNow.Hour * 60 + localNow.Minute - 30) % 1440 + 1440) % 1440,
                DurationMinutes = 120,
                TimeZoneId = "Europe/Moscow"
            }
        };

        await WithScheduleAsync(manager, schedule, async () =>
        {
            var today = DateOnly.FromDateTime(localNow);
            await DeleteDayAsync(today);
            var pastDate = NewDate();
            var lateDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(100);
            var now = DateTime.UtcNow;
            await SeedDayAsync(pastDate, Item(1, null, now.AddHours(-4), now.AddHours(-2)));
            await SeedDayAsync(lateDate, Item(1, null, now.AddHours(8), now.AddHours(10)));

            var virtualDay = await GetDayAsync(manager, today);
            var virtualItem = Item(virtualDay, 1);
            virtualDay.GetProperty("isSaved").GetBoolean().Should().BeFalse();
            virtualItem.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
            virtualItem.GetProperty("closureStatus").ValueKind.Should().Be(JsonValueKind.Null);
            virtualItem.GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse();
            virtualItem.GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeTrue();

            var lateItem = Item(await GetDayAsync(manager, lateDate), 1);
            lateItem.GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeFalse(
                "текущая виртуальная позиция вне запрошенного диапазона ближе");
            var pastItem = Item(await GetDayAsync(manager, pastDate), 1);
            pastItem.GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeFalse();
        });
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"plan_indicator_{role}_{Guid.NewGuid():N}";
        const string password = "Passw0rd!";
        var user = new User
        {
            UserName = login,
            Email = $"{login}@test.local",
            FullName = $"{role} {Guid.NewGuid():N}",
            EmployeeId = $"EMP-{Guid.NewGuid():N}"[..12],
            Role = role,
            IsActive = true
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password);
    }

    private static DateOnly NewDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-Interlocked.Increment(ref _dateOffset));

    private static ShiftPlanItem Item(int position, string? leadId, DateTime start, DateTime end) => new()
    {
        Position = position,
        ShiftLeadId = leadId,
        StartAtUtc = Utc(start),
        EndAtUtc = Utc(end),
        TimeZoneId = "Europe/Moscow"
    };

    private async Task<ShiftPlanDay> SeedDayAsync(DateOnly date, params ShiftPlanItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay { ShiftDate = date };
        foreach (var item in items)
            day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return day;
    }

    private async Task<ShiftTask> SeedTaskAsync(Guid immId, DateTime start, DateTime end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var planned = Utc(start);
        var local = schedule.ToLocal(planned);
        var task = new ShiftTask
        {
            ImmId = immId,
            MoldId = _factory.TestMoldId,
            ProductId = _factory.TestProductId,
            PlanQuantity = 10,
            Cavities = 1,
            PartWeightGrams = 5,
            PackagingUnit = 20,
            Status = TaskStatus.Completed,
            PlannedDate = DateOnly.FromDateTime(local),
            PlannedSetupStartTime = TimeOnly.FromDateTime(local),
            PlannedSetupMinutes = 15,
            PlannedFullCycleSeconds = 60,
            SetupStartedAt = planned,
            StartedAt = planned,
            CompletedAt = Utc(end)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private async Task SeedCycleAsync(ShiftTask task, DateTime endTime, int cavities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var end = Utc(endTime);
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = task.ImmId,
            TaskId = task.Id,
            MoldId = task.MoldId,
            StartTime = end.AddMinutes(-1),
            EndTime = end,
            DurationSeconds = 60,
            Cavities = cavities,
            IsSuccessful = true
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedClosureAsync(Guid itemId, string leadId, ShiftClosureState state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftClosures.Add(new ShiftClosure
        {
            ShiftPlanItemId = itemId,
            State = state,
            CreatedById = leadId,
            ClosedById = state == ShiftClosureState.Closed ? leadId : null,
            ClosedAt = state == ShiftClosureState.Closed ? Utc(DateTime.UtcNow) : null
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> GetDayAsync(HttpClient client, DateOnly date)
    {
        var response = await client.GetAsync(
            $"/api/shift-plan?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var days = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return days.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static JsonElement Item(JsonElement day, int position) =>
        day.GetProperty("items").EnumerateArray()
            .Should().ContainSingle(i => i.GetProperty("position").GetInt32() == position).Subject;

    private static Task<HttpResponseMessage> SaveScheduleAsync(
        HttpClient client, IReadOnlyList<ShiftScheduleItemDto> shifts) =>
        client.PutAsJsonAsync("/api/shifts", new SaveShiftsRequestDto { Shifts = shifts.ToList() });

    private async Task WithScheduleAsync(
        HttpClient client, List<ShiftScheduleItemDto> schedule, Func<Task> test)
    {
        var original = await client.GetFromJsonAsync<List<ShiftDto>>("/api/shifts", JsonOptions);
        (await SaveScheduleAsync(client, schedule)).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            await test();
        }
        finally
        {
            var restore = original!.Select(s => new ShiftScheduleItemDto
            {
                StartMinutes = s.StartMinutes,
                DurationMinutes = s.DurationMinutes,
                BreakStartMinutes = s.BreakStartMinutes,
                BreakDurationMinutes = s.BreakDurationMinutes,
                TimeZoneId = s.TimeZoneId
            }).ToList();
            (await SaveScheduleAsync(client, restore)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
