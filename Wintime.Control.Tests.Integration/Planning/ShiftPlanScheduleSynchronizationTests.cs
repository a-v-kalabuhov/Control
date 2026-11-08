using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-32: замена справочника смен синхронизирует только сохранённые будущие дни.</summary>
[Collection("Integration")]
public class ShiftPlanScheduleSynchronizationTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 300;

    private readonly IntegrationTestFactory _factory;

    public ShiftPlanScheduleSynchronizationTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);

    private sealed record AssignmentSnapshot(
        Guid Id, Guid ShiftPlanItemId, Guid ImmId, string OperatorId, int Slot,
        DateTime AssignedAt, DateTime? UnassignedAt, DateTime CreatedAt);

    private sealed record ItemSnapshot(
        Guid Id, Guid ShiftPlanDayId, int Position, DateTime StartAtUtc, DateTime EndAtUtc,
        string TimeZoneId, string? ShiftLeadId, DateTime CreatedAt,
        IReadOnlyList<AssignmentSnapshot> Assignments);

    private sealed record DaySnapshot(
        Guid Id, DateOnly ShiftDate, DateTime CreatedAt, IReadOnlyList<ItemSnapshot> Items);

    private sealed record ShiftSnapshot(
        Guid Id, int StartMinutes, int DurationMinutes, int BreakStartMinutes,
        int BreakDurationMinutes, string TimeZoneId, DateTime CreatedAt);

    private static DateOnly NewFutureDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset));

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerClientAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdminClientAsync() => ClientAsync("admin", "Admin123!");
    private Task<HttpClient> AdjusterClientAsync() => ClientAsync("test_adjuster", "Adjuster123!");

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"shift_sync_{role}_{Guid.NewGuid():N}";
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

    private static List<ShiftScheduleItemDto> Schedule(params int[] startMinutes) =>
        startMinutes.Select(start => new ShiftScheduleItemDto
        {
            StartMinutes = start,
            DurationMinutes = 480,
            TimeZoneId = "Europe/Moscow"
        }).ToList();

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

    private async Task<ShiftPlanDay> SeedDayAsync(
        DateOnly date, string leadId, string operatorId, int positionCount)
    {
        await DeleteDayAsync(date);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay { ShiftDate = date };
        for (var position = 1; position <= positionCount; position++)
        {
            var item = new ShiftPlanItem
            {
                Position = position,
                StartAtUtc = DateTime.UtcNow.AddDays(10).AddHours(position),
                EndAtUtc = DateTime.UtcNow.AddDays(10).AddHours(position + 8),
                TimeZoneId = "Europe/Moscow",
                ShiftLeadId = leadId
            };
            item.OperatorAssignments.Add(new ShiftImmOperatorAssignment
            {
                ShiftPlanItem = item,
                ImmId = _factory.TestImmId,
                OperatorId = operatorId,
                Slot = 1,
                AssignedAt = DateTime.UtcNow.AddHours(-1)
            });
            day.Items.Add(item);
        }

        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return day;
    }

    private async Task<DaySnapshot> LoadDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = await db.ShiftPlanDays.AsNoTracking()
            .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
            .SingleAsync(d => d.ShiftDate == date);
        return Snapshot(day);
    }

    private static DaySnapshot Snapshot(ShiftPlanDay day) => new(
        day.Id,
        day.ShiftDate,
        day.CreatedAt,
        day.Items.OrderBy(i => i.Position).Select(i => new ItemSnapshot(
            i.Id,
            i.ShiftPlanDayId,
            i.Position,
            i.StartAtUtc,
            i.EndAtUtc,
            i.TimeZoneId,
            i.ShiftLeadId,
            i.CreatedAt,
            i.OperatorAssignments.OrderBy(a => a.Id).Select(a => new AssignmentSnapshot(
                a.Id, a.ShiftPlanItemId, a.ImmId, a.OperatorId, a.Slot,
                a.AssignedAt, a.UnassignedAt, a.CreatedAt)).ToList())).ToList());

    private async Task<IReadOnlyList<ShiftWindow>> SavedWindowsAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var shifts = await db.Shifts.AsNoTracking().ToListAsync();
        return ShiftWindowCalculator.Build(date, shifts);
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    [Fact]
    public async Task FutureDay_ShrinkAndExtend_PreservesRemainingGraphAndRemovesOrAddsColumns()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var date = NewFutureDate();
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, Schedule(0, 480, 960), async () =>
        {
            var seeded = await SeedDayAsync(date, lead.Id, @operator.Id, 3);
            var originalItems = seeded.Items.OrderBy(i => i.Position).ToList();
            var originalIds = originalItems.Select(i => i.Id).ToList();
            var firstId = originalItems[0].Id;
            var firstAssignmentId = originalItems[0].OperatorAssignments.Single().Id;

            var shrunkResponse = await SaveScheduleAsync(client, Schedule(360));
            shrunkResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                await shrunkResponse.Content.ReadAsStringAsync());

            var shrunk = await LoadDayAsync(date);
            shrunk.Items.Should().ContainSingle();
            shrunk.Items[0].Id.Should().Be(firstId);
            shrunk.Items[0].ShiftLeadId.Should().Be(lead.Id);
            shrunk.Items[0].Assignments.Should().ContainSingle()
                .Which.Id.Should().Be(firstAssignmentId);
            var shrunkWindows = await SavedWindowsAsync(date);
            shrunk.Items[0].StartAtUtc.Should().Be(shrunkWindows[0].StartAtUtc);
            shrunk.Items[0].EndAtUtc.Should().Be(shrunkWindows[0].EndAtUtc);
            shrunk.Items[0].TimeZoneId.Should().Be(shrunkWindows[0].TimeZoneId);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await db.ShiftPlanItems.CountAsync(i => originalIds.Skip(1).Contains(i.Id))).Should().Be(0);
                (await db.ShiftImmOperatorAssignments.CountAsync(a =>
                    originalItems.Skip(1).Select(i => i.OperatorAssignments.Single().Id).Contains(a.Id)))
                    .Should().Be(0);
            }

            var extendedResponse = await SaveScheduleAsync(client, Schedule(120, 600, 1080));
            extendedResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                await extendedResponse.Content.ReadAsStringAsync());

            var extended = await LoadDayAsync(date);
            extended.Items.Select(i => i.Position).Should().Equal(1, 2, 3);
            extended.Items[0].Id.Should().Be(firstId);
            extended.Items[0].ShiftLeadId.Should().Be(lead.Id);
            extended.Items[0].Assignments.Should().ContainSingle()
                .Which.Id.Should().Be(firstAssignmentId);
            extended.Items.Skip(1).Should().OnlyContain(i =>
                i.ShiftLeadId == null && i.Assignments.Count == 0 && !originalIds.Contains(i.Id));
            var extendedWindows = await SavedWindowsAsync(date);
            extended.Items.Select(i => i.StartAtUtc).Should().Equal(extendedWindows.Select(w => w.StartAtUtc));
            extended.Items.Select(i => i.EndAtUtc).Should().Equal(extendedWindows.Select(w => w.EndAtUtc));
            extended.Items.Select(i => i.TimeZoneId).Should().Equal(extendedWindows.Select(w => w.TimeZoneId));
        });
    }

    [Fact]
    public async Task FactoryTodayAndPastSavedDays_AreUntouchedEvenWhenTodayShiftHasNotStarted()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var yesterday = today.AddDays(-1);
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, Schedule(480, 960), async () =>
        {
            await SeedDayAsync(today, lead.Id, @operator.Id, 2);
            await SeedDayAsync(yesterday, lead.Id, @operator.Id, 2);
            var todayBefore = await LoadDayAsync(today);
            var yesterdayBefore = await LoadDayAsync(yesterday);
            todayBefore.Items.Should().OnlyContain(i => i.StartAtUtc > DateTime.UtcNow);

            var response = await SaveScheduleAsync(client, Schedule(60, 600));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            (await LoadDayAsync(today)).Should().BeEquivalentTo(todayBefore);
            (await LoadDayAsync(yesterday)).Should().BeEquivalentTo(yesterdayBefore);
        });
    }

    [Fact]
    public async Task VirtualCurrentAndPastDays_AfterScheduleChange_UseNewWindows()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
        var yesterday = today.AddDays(-1);
        var client = await ManagerClientAsync();
        await DeleteDayAsync(today);
        await DeleteDayAsync(yesterday);

        await WithScheduleAsync(client, Schedule(0, 480), async () =>
        {
            var changed = Schedule(120, 720);
            (await SaveScheduleAsync(client, changed)).StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await RequiredJsonAsync(await client.GetAsync(
                $"/api/shift-plan?from={yesterday:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
            var days = body.EnumerateArray().ToList();
            days.Should().HaveCount(2);
            days.Should().OnlyContain(d => !d.GetProperty("isSaved").GetBoolean());
            foreach (var day in days)
            {
                var date = DateOnly.Parse(day.GetProperty("shiftDate").GetString()!);
                var expected = await SavedWindowsAsync(date);
                var items = day.GetProperty("items").EnumerateArray().ToList();
                items.Select(i => i.GetProperty("position").GetInt32()).Should().Equal(1, 2);
                items.Select(i => i.GetProperty("startAtUtc").GetDateTime()).Should()
                    .Equal(expected.Select(w => w.StartAtUtc));
                items.Select(i => i.GetProperty("endAtUtc").GetDateTime()).Should()
                    .Equal(expected.Select(w => w.EndAtUtc));
                items.Select(i => i.GetProperty("timeZoneId").GetString()).Should()
                    .Equal(expected.Select(w => w.TimeZoneId));
            }
        });
    }

    [Fact]
    public async Task FutureDay_WithoutRemainingLeadsOrAssignments_IsDeleted()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewFutureDate();
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, Schedule(0, 480), async () =>
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                db.ShiftPlanDays.Add(new ShiftPlanDay
                {
                    ShiftDate = date,
                    Items =
                    [
                        new ShiftPlanItem
                        {
                            Position = 1,
                            StartAtUtc = DateTime.UtcNow.AddDays(2),
                            EndAtUtc = DateTime.UtcNow.AddDays(2).AddHours(8),
                            TimeZoneId = "Europe/Moscow"
                        },
                        new ShiftPlanItem
                        {
                            Position = 2,
                            StartAtUtc = DateTime.UtcNow.AddDays(2).AddHours(8),
                            EndAtUtc = DateTime.UtcNow.AddDays(2).AddHours(16),
                            TimeZoneId = "Europe/Moscow",
                            ShiftLeadId = lead.Id
                        }
                    ]
                });
                await db.SaveChangesAsync();
            }

            (await SaveScheduleAsync(client, Schedule(120))).StatusCode.Should().Be(HttpStatusCode.OK);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(0);
                (await db.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == date)).Should().Be(0);
            }

            var body = await RequiredJsonAsync(await client.GetAsync(
                $"/api/shift-plan?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));
            var virtualDay = body.EnumerateArray().Should().ContainSingle().Subject;
            virtualDay.GetProperty("isSaved").GetBoolean().Should().BeFalse();
            virtualDay.GetProperty("items").EnumerateArray().Should().ContainSingle()
                .Which.GetProperty("shiftLeadId").ValueKind.Should().Be(JsonValueKind.Null);
        });
    }

    [Fact]
    public async Task SynchronizationFailure_RollsBackScheduleAndFutureDayTogether()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var date = NewFutureDate();
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, Schedule(0, 480), async () =>
        {
            var seeded = await SeedDayAsync(date, lead.Id, @operator.Id, 2);
            var beforeDay = await LoadDayAsync(date);
            List<ShiftSnapshot> beforeSchedule;
            string connectionString;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                connectionString = db.Database.GetDbConnection().ConnectionString;
                beforeSchedule = await db.Shifts.AsNoTracking().OrderBy(s => s.StartMinutes)
                    .Select(s => new ShiftSnapshot(
                        s.Id, s.StartMinutes, s.DurationMinutes, s.BreakStartMinutes,
                        s.BreakDurationMinutes, s.TimeZoneId, s.CreatedAt)).ToListAsync();
            }

            var suffix = Guid.NewGuid().ToString("N");
            var functionName = $"fail_shift_sync_{suffix}";
            var triggerName = $"fail_shift_sync_{suffix}";
            var itemId = seeded.Items.Single(i => i.Position == 1).Id;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand($"""
                CREATE FUNCTION "{functionName}"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'forced shift plan synchronization failure'; END;
                $$;
                CREATE TRIGGER "{triggerName}" BEFORE UPDATE ON "ShiftPlanItems"
                FOR EACH ROW WHEN (OLD."Id" = '{itemId}')
                EXECUTE FUNCTION "{functionName}"();
                """, connection))
                await command.ExecuteNonQueryAsync();

            try
            {
                var response = await SaveScheduleAsync(client, Schedule(120, 600));
                response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
                    await response.Content.ReadAsStringAsync());

                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var afterSchedule = await db.Shifts.AsNoTracking().OrderBy(s => s.StartMinutes)
                    .Select(s => new ShiftSnapshot(
                        s.Id, s.StartMinutes, s.DurationMinutes, s.BreakStartMinutes,
                        s.BreakDurationMinutes, s.TimeZoneId, s.CreatedAt)).ToListAsync();
                afterSchedule.Should().BeEquivalentTo(beforeSchedule);
                (await LoadDayAsync(date)).Should().BeEquivalentTo(beforeDay);
            }
            finally
            {
                await using var command = new NpgsqlCommand($"""
                    DROP TRIGGER IF EXISTS "{triggerName}" ON "ShiftPlanItems";
                    DROP FUNCTION IF EXISTS "{functionName}"();
                    """, connection);
                await command.ExecuteNonQueryAsync();
            }
        });
    }

    [Fact]
    public async Task SaveSchedule_KeepsManagerAdminAuthorizationAndValidationErrors()
    {
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();
        var adjuster = await AdjusterClientAsync();
        var empty = new SaveShiftsRequestDto { Shifts = [] };

        foreach (var client in new[] { manager, admin })
        {
            var response = await client.PutAsJsonAsync("/api/shifts", empty);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            body.GetProperty("errors").EnumerateArray().Should().ContainSingle()
                .Which.GetString().Should().Be("Список смен не может быть пустым");
        }

        (await adjuster.PutAsJsonAsync("/api/shifts", empty)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var invalid = new SaveShiftsRequestDto
        {
            Shifts =
            [
                new ShiftScheduleItemDto
                {
                    StartMinutes = 480, DurationMinutes = 480, TimeZoneId = "Europe/Moscow"
                },
                new ShiftScheduleItemDto
                {
                    StartMinutes = 900, DurationMinutes = 480, TimeZoneId = "Europe/Moscow"
                }
            ]
        };
        var invalidResponse = await manager.PutAsJsonAsync("/api/shifts", invalid);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidBody = await invalidResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        invalidBody.GetProperty("errors").EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Contain("Смены пересекаются");
    }
}
