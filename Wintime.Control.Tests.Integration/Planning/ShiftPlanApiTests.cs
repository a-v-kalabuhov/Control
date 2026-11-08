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

/// <summary>PZP-32: виртуальные дни, материализация полного дня и старшие позиций смен.</summary>
[Collection("Integration")]
public class ShiftPlanApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 100;

    private readonly IntegrationTestFactory _factory;

    public ShiftPlanApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password, string FullName);

    private static DateOnly NewDate() =>
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

    private async Task<TestUser> SeedUserAsync(UserRole role, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"shift_plan_{role}_{Guid.NewGuid():N}";
        const string password = "Passw0rd!";
        var fullName = $"{role} {Guid.NewGuid():N}";
        var user = new User
        {
            UserName = login,
            Email = $"{login}@test.local",
            FullName = fullName,
            EmployeeId = $"EMP-{Guid.NewGuid():N}"[..12],
            Role = role,
            IsActive = isActive
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password, fullName);
    }

    private async Task<string> TestManagerIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return (await users.FindByNameAsync("test_manager"))!.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static Task<HttpResponseMessage> PutLeadAsync(
        HttpClient client, DateOnly date, int position, string? shiftLeadId) =>
        client.PutAsJsonAsync($"/api/shift-plan/days/{date:yyyy-MM-dd}/items/{position}/lead",
            new { shiftLeadId });

    private static async Task<JsonElement> GetDayAsync(HttpClient client, DateOnly date)
    {
        var days = await RequiredJsonAsync(await client.GetAsync(
            $"/api/shift-plan?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));
        return days.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static JsonElement Item(JsonElement day, int position) =>
        day.GetProperty("items").EnumerateArray()
            .Should().ContainSingle(i => i.GetProperty("position").GetInt32() == position).Subject;

    private async Task<ShiftPlanDay> SeedDayAsync(DateOnly date, params ShiftPlanItem[] items)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay { ShiftDate = date };
        foreach (var item in items.OrderBy(i => i.Position))
            day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return day;
    }

    private static ShiftPlanItem FutureItem(int position, string? leadId = null, int hoursFromNow = 2) => new()
    {
        Position = position,
        StartAtUtc = DateTime.UtcNow.AddHours(hoursFromNow),
        EndAtUtc = DateTime.UtcNow.AddHours(hoursFromNow + 1),
        TimeZoneId = "Europe/Moscow",
        ShiftLeadId = leadId
    };

    private static List<ShiftScheduleItemDto> TwoShifts() =>
    [
        new() { StartMinutes = 480, DurationMinutes = 480, TimeZoneId = "Europe/Moscow" },
        new() { StartMinutes = 960, DurationMinutes = 480, TimeZoneId = "Europe/Moscow" }
    ];

    private static Task<HttpResponseMessage> SaveScheduleAsync(
        HttpClient client, List<ShiftScheduleItemDto> shifts) =>
        client.PutAsJsonAsync("/api/shifts", new SaveShiftsRequestDto { Shifts = shifts });

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

    [Fact]
    public async Task Get_ReturnsEveryVirtualDateOnceWithServerWindowsAndFactoryToday()
    {
        var client = await ManagerClientAsync();
        await WithScheduleAsync(client, TwoShifts(), async () =>
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow")));
            var from = today.AddDays(-1);
            var to = today.AddDays(1);

            var days = await RequiredJsonAsync(await client.GetAsync(
                $"/api/shift-plan?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

            var actualDates = days.EnumerateArray().Select(d => d.GetProperty("shiftDate").GetString()).ToList();
            actualDates.Should().Equal(
                from.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
            foreach (var day in days.EnumerateArray())
            {
                day.GetProperty("isSaved").GetBoolean().Should().BeFalse();
                day.GetProperty("isFactoryToday").GetBoolean().Should()
                    .Be(day.GetProperty("shiftDate").GetString() == today.ToString("yyyy-MM-dd"));
                var items = day.GetProperty("items").EnumerateArray().ToList();
                items.Select(i => i.GetProperty("position").GetInt32()).Should().Equal(1, 2);
                items.Should().OnlyContain(i => i.GetProperty("id").ValueKind == JsonValueKind.Null);
                items.Should().OnlyContain(i => !i.GetProperty("canEditOperators").GetBoolean());
                var shiftDate = DateOnly.Parse(day.GetProperty("shiftDate").GetString()!);
                var scheduleEntities = TwoShifts().Select(s => new Shift
                {
                    StartMinutes = s.StartMinutes,
                    DurationMinutes = s.DurationMinutes,
                    TimeZoneId = s.TimeZoneId
                }).ToList();
                var expected = ShiftWindowCalculator.Build(shiftDate, scheduleEntities);
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
    public async Task Get_FromAfterTo_Returns400()
    {
        var client = await ManagerClientAsync();
        var response = await client.GetAsync("/api/shift-plan?from=2026-11-10&to=2026-11-08");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(response)).GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LeadCandidates_ContainOnlyActiveLeadsAndManagersWithExactDtoShape()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var admin = await SeedUserAsync(UserRole.Admin);
        var adjuster = await SeedUserAsync(UserRole.Adjuster);
        var observer = await SeedUserAsync(UserRole.Observer);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var archivedLead = await SeedUserAsync(UserRole.ShiftLead, isActive: false);
        var managerId = await TestManagerIdAsync();
        var client = await ManagerClientAsync();

        var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-plan/lead-candidates"));
        var candidates = body.EnumerateArray().ToList();
        var ids = candidates.Select(c => c.GetProperty("id").GetString()).ToList();

        ids.Should().Contain(lead.Id).And.Contain(managerId);
        ids.Should().NotContain(new[] { admin.Id, adjuster.Id, observer.Id, @operator.Id, archivedLead.Id });
        candidates.Should().OnlyContain(c => c.EnumerateObject().Select(p => p.Name)
            .OrderBy(n => n).SequenceEqual(new[] { "employeeId", "fullName", "id" }));
        candidates.Single(c => c.GetProperty("id").GetString() == lead.Id)
            .GetProperty("fullName").GetString().Should().Be(lead.FullName);
    }

    [Fact]
    public async Task FirstLeadAssignment_MaterializesAllPositionsAndFutureSavedWindowsFollowScheduleChange()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, TwoShifts(), async () =>
        {
            var response = await PutLeadAsync(client, date, 2, lead.Id);
            var day = await RequiredJsonAsync(response);

            day.GetProperty("isSaved").GetBoolean().Should().BeTrue();
            var items = day.GetProperty("items").EnumerateArray().ToList();
            items.Select(i => i.GetProperty("position").GetInt32()).Should().Equal(1, 2);
            items.Should().OnlyContain(i => i.GetProperty("id").ValueKind == JsonValueKind.String);
            Item(day, 1).GetProperty("shiftLeadId").ValueKind.Should().Be(JsonValueKind.Null);
            Item(day, 2).GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
            var itemIds = items.ToDictionary(
                i => i.GetProperty("position").GetInt32(),
                i => i.GetProperty("id").GetGuid());

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(1);
                (await db.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == date)).Should().Be(2);
            }

            var changed = TwoShifts();
            changed[0].StartMinutes = 420;
            changed[0].DurationMinutes = 540;
            changed[1].StartMinutes = 1020;
            changed[1].DurationMinutes = 420;
            (await SaveScheduleAsync(client, changed)).StatusCode.Should().Be(HttpStatusCode.OK);

            var reread = await GetDayAsync(client, date);
            reread.GetProperty("isSaved").GetBoolean().Should().BeTrue();
            var expected = ShiftWindowCalculator.Build(date, changed.Select(s => new Shift
            {
                StartMinutes = s.StartMinutes,
                DurationMinutes = s.DurationMinutes,
                TimeZoneId = s.TimeZoneId
            }).ToList());
            var rereadItems = reread.GetProperty("items").EnumerateArray().ToList();
            rereadItems.Select(i => new
            {
                Position = i.GetProperty("position").GetInt32(),
                Start = i.GetProperty("startAtUtc").GetDateTime(),
                End = i.GetProperty("endAtUtc").GetDateTime()
            }).Should().Equal(expected.Select(w => new
            {
                Position = w.Position,
                Start = w.StartAtUtc,
                End = w.EndAtUtc
            }));
            rereadItems.Select(i => i.GetProperty("id").GetGuid()).Should()
                .Equal(itemIds[1], itemIds[2]);
            Item(reread, 2).GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
        });
    }

    [Fact]
    public async Task LeadReplacement_PreservesItemAndAssignmentsAndManagerCanAssignSelf()
    {
        var oldLead = await SeedUserAsync(UserRole.ShiftLead);
        var newLead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var admin = await SeedUserAsync(UserRole.Admin);
        var archivedLead = await SeedUserAsync(UserRole.ShiftLead, isActive: false);
        var adjuster = await SeedUserAsync(UserRole.Adjuster);
        var managerId = await TestManagerIdAsync();
        var date = NewDate();
        var day = await SeedDayAsync(date, FutureItem(1, oldLead.Id), FutureItem(2));
        var item = day.Items.Single(i => i.Position == 1);
        var assignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = _factory.TestImmId,
            OperatorId = @operator.Id,
            Slot = 1,
            AssignedAt = DateTime.UtcNow
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }
        var client = await ManagerClientAsync();

        var replaced = await RequiredJsonAsync(await PutLeadAsync(client, date, 1, newLead.Id));
        Item(replaced, 1).GetProperty("id").GetGuid().Should().Be(item.Id);
        Item(replaced, 1).GetProperty("shiftLeadId").GetString().Should().Be(newLead.Id);
        Item(replaced, 1).GetProperty("shiftLeadName").GetString().Should().Be(newLead.FullName);
        Item(replaced, 1).GetProperty("operatorAssignmentCount").GetInt32().Should().Be(1);

        var selfAssigned = await RequiredJsonAsync(await PutLeadAsync(client, date, 1, managerId));
        Item(selfAssigned, 1).GetProperty("id").GetGuid().Should().Be(item.Id);
        Item(selfAssigned, 1).GetProperty("shiftLeadId").GetString().Should().Be(managerId);
        Item(selfAssigned, 1).GetProperty("operatorAssignmentCount").GetInt32().Should().Be(1);

        foreach (var invalid in new[] { admin.Id, archivedLead.Id, adjuster.Id, Guid.NewGuid().ToString() })
            (await PutLeadAsync(client, date, 1, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftImmOperatorAssignments.SingleAsync(a => a.Id == assignment.Id)).OperatorId
                .Should().Be(@operator.Id);
            (await db.ShiftPlanItems.SingleAsync(i => i.Id == item.Id)).ShiftLeadId.Should().Be(managerId);
        }
    }

    [Fact]
    public async Task SetLead_AtOrAfterStartAndForHistoricalVirtualDate_Returns400()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var now = DateTime.UtcNow;
        var day = await SeedDayAsync(date,
            new ShiftPlanItem
            {
                Position = 1, StartAtUtc = now.AddHours(-3), EndAtUtc = now.AddHours(-2),
                TimeZoneId = "Europe/Moscow"
            },
            new ShiftPlanItem
            {
                Position = 2, StartAtUtc = now.AddHours(-1), EndAtUtc = now.AddHours(1),
                TimeZoneId = "Europe/Moscow"
            },
            new ShiftPlanItem
            {
                Position = 3, StartAtUtc = now, EndAtUtc = now.AddHours(1),
                TimeZoneId = "Europe/Moscow"
            },
            FutureItem(4));
        var client = await ManagerClientAsync();

        var before = await GetDayAsync(client, date);
        Item(before, 1).GetProperty("state").GetString().Should().Be("Historical");
        Item(before, 1).GetProperty("canEditLead").GetBoolean().Should().BeFalse();
        Item(before, 2).GetProperty("state").GetString().Should().Be("Current");
        Item(before, 2).GetProperty("canEditLead").GetBoolean().Should().BeFalse();
        Item(before, 3).GetProperty("canEditLead").GetBoolean().Should().BeFalse();
        Item(before, 4).GetProperty("state").GetString().Should().Be("Future");
        Item(before, 4).GetProperty("canEditLead").GetBoolean().Should().BeTrue();

        foreach (var position in new[] { 1, 2, 3 })
            (await PutLeadAsync(client, date, position, lead.Id)).StatusCode.Should()
                .Be(HttpStatusCode.BadRequest);
        (await PutLeadAsync(client, date, 4, lead.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var historicalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        (await PutLeadAsync(client, historicalDate, 1, lead.Id)).StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);
        (await GetDayAsync(client, historicalDate)).GetProperty("isSaved").GetBoolean().Should().BeFalse();
        day.Items.Should().HaveCount(4);
    }

    [Fact]
    public async Task ClearLead_BlocksOnAnyAssignmentsAndLastClearDeletesEmptyDay()
    {
        var lead1 = await SeedUserAsync(UserRole.ShiftLead);
        var lead2 = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var date = NewDate();
        var day = await SeedDayAsync(date, FutureItem(1, lead1.Id), FutureItem(2, lead2.Id));
        var item1 = day.Items.Single(i => i.Position == 1);
        var assignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item1.Id,
            ImmId = _factory.TestImmId,
            OperatorId = @operator.Id,
            Slot = 1,
            AssignedAt = DateTime.UtcNow.AddHours(-2),
            UnassignedAt = DateTime.UtcNow.AddHours(-1)
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }
        var client = await ManagerClientAsync();

        (await PutLeadAsync(client, date, 1, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetDayAsync(client, date)).GetProperty("isSaved").GetBoolean().Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.RemoveRange(
                db.ShiftImmOperatorAssignments.Where(a => a.ShiftPlanItemId == item1.Id));
            await db.SaveChangesAsync();
        }

        var cleared = await RequiredJsonAsync(await PutLeadAsync(client, date, 1, null));
        cleared.GetProperty("isSaved").GetBoolean().Should().BeTrue();
        Item(cleared, 1).GetProperty("shiftLeadId").ValueKind.Should().Be(JsonValueKind.Null);
        Item(cleared, 2).GetProperty("shiftLeadId").GetString().Should().Be(lead2.Id);

        var lastClear = await RequiredJsonAsync(await PutLeadAsync(client, date, 2, null));
        lastClear.GetProperty("isSaved").GetBoolean().Should().BeFalse();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(0);
            (await db.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == date)).Should().Be(0);
        }
        var virtualDay = await GetDayAsync(client, date);
        lastClear.GetProperty("items").GetRawText().Should().Be(virtualDay.GetProperty("items").GetRawText(),
            "после удаления сохранённого дня PUT возвращает тот же виртуальный день, что и GET");
        virtualDay.GetProperty("isSaved").GetBoolean().Should().BeFalse();
        virtualDay.GetProperty("items").EnumerateArray().Should()
            .OnlyContain(i => i.GetProperty("id").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ConcurrentVirtualDayMaterialization_ReturnsOneSuccessAndOne409WithoutDuplicates()
    {
        var lead1 = await SeedUserAsync(UserRole.ShiftLead);
        var lead2 = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();

        await WithScheduleAsync(manager, TwoShifts(), async () =>
        {
            string connectionString;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                connectionString = db.Database.GetDbConnection().ConnectionString;
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var lockCommand = new NpgsqlCommand(
                "LOCK TABLE \"ShiftPlanDays\" IN EXCLUSIVE MODE", connection, transaction))
                await lockCommand.ExecuteNonQueryAsync();

            var requests = Task.WhenAll(
                PutLeadAsync(manager, date, 1, lead1.Id),
                PutLeadAsync(admin, date, 2, lead2.Id));
            try
            {
                var blocked = 0;
                for (var attempt = 0; attempt < 100 && blocked < 2; attempt++)
                {
                    await using var command = new NpgsqlCommand("""
                        SELECT count(*) FROM pg_locks
                        WHERE relation = '"ShiftPlanDays"'::regclass
                          AND mode = 'RowExclusiveLock'
                          AND NOT granted
                        """, connection, transaction);
                    blocked = Convert.ToInt32(await command.ExecuteScalarAsync());
                    if (blocked < 2) await Task.Delay(50);
                }
                blocked.Should().Be(2, "оба запроса должны одновременно ожидать материализацию виртуального дня");
            }
            finally
            {
                await transaction.CommitAsync();
            }

            var responses = await requests;
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
            var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            (await JsonAsync(conflict)).GetProperty("message").GetString().Should()
                .Be("План смены уже изменён другим пользователем");

            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await verifyDb.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(1);
            (await verifyDb.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == date)).Should().Be(2);
        });
    }

    [Fact]
    public async Task ConcurrentLastLeadClearAndReassign_Returns409WhenSavedDayWasDeleted()
    {
        var oldLead = await SeedUserAsync(UserRole.ShiftLead);
        var newLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        await SeedDayAsync(date, FutureItem(1, oldLead.Id));
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();

        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE \"ShiftPlanItems\" IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync();

        var clearRequest = PutLeadAsync(manager, date, 1, null);
        var reassignRequest = Task.FromResult<HttpResponseMessage>(null!);
        var committed = false;
        try
        {
            await WaitForBlockedLockAsync("ShiftPlanItems", "relation");
            reassignRequest = PutLeadAsync(admin, date, 1, newLead.Id);
            await WaitForGrantedDayLocksAsync();
            await transaction.CommitAsync();
            committed = true;
        }
        finally
        {
            if (!committed) await transaction.CommitAsync();
        }

        var responses = await Task.WhenAll(clearRequest, reassignRequest);
        responses[0].StatusCode.Should().Be(HttpStatusCode.OK);
        responses[1].StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(responses[1])).GetProperty("message").GetString().Should()
            .Be("План смены уже изменён другим пользователем");

        async Task WaitForBlockedLockAsync(string table, string lockType)
        {
            var blocked = 0;
            for (var attempt = 0; attempt < 100 && blocked < 1; attempt++)
            {
                await using var command = new NpgsqlCommand($"""
                    SELECT count(*) FROM pg_locks
                    WHERE relation = '"{table}"'::regclass
                      AND locktype = '{lockType}'
                      AND NOT granted
                    """, connection, transaction);
                blocked = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (blocked < 1) await Task.Delay(50);
            }
            blocked.Should().Be(1, "очистка должна удерживать день до блокировки переназначения");
        }

        async Task WaitForGrantedDayLocksAsync()
        {
            var locked = 0;
            for (var attempt = 0; attempt < 100 && locked < 2; attempt++)
            {
                await using var command = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_locks
                    WHERE relation = '"ShiftPlanDays"'::regclass
                      AND locktype = 'relation'
                      AND mode = 'RowShareLock'
                      AND granted
                    """, connection, transaction);
                locked = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (locked < 2) await Task.Delay(50);
            }
            locked.Should().Be(2, "оба запроса должны дойти до блокировки одного сохранённого дня");
        }
    }

    [Fact]
    public async Task SetLead_UnknownSavedOrVirtualPosition_Returns404()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        await SeedDayAsync(date, FutureItem(1));
        var client = await ManagerClientAsync();

        var saved = await PutLeadAsync(client, date, 99, lead.Id);
        saved.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(saved)).GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        var virtualDate = NewDate();
        (await PutLeadAsync(client, virtualDate, 99, lead.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CanEditOperators_UsesNearestSavedItemOutsideRequestedRange()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var now = DateTime.UtcNow;
        var nearestDate = NewDate();
        var laterDate = NewDate();
        var nearest = await SeedDayAsync(nearestDate, new ShiftPlanItem
        {
            Position = 1,
            StartAtUtc = now.AddHours(-1),
            EndAtUtc = now.AddHours(1),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = lead.Id
        });
        var later = await SeedDayAsync(laterDate, new ShiftPlanItem
        {
            Position = 1,
            StartAtUtc = now.AddHours(2),
            EndAtUtc = now.AddHours(3),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = lead.Id
        });
        var client = await ManagerClientAsync();

        var laterOnly = await GetDayAsync(client, laterDate);
        Item(laterOnly, 1).GetProperty("canEditOperators").GetBoolean().Should().BeFalse(
            "текущая смена той же позиции находится вне фильтра, но она ближайшая");
        var nearestOnly = await GetDayAsync(client, nearestDate);
        Item(nearestOnly, 1).GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

        nearest.Items.Single().Id.Should().NotBe(later.Items.Single().Id);
    }

    [Fact]
    public async Task ShiftLeadAdjusterObserverOperator_CannotUseAnyShiftPlanEndpoint()
    {
        foreach (var role in new[] { UserRole.ShiftLead, UserRole.Adjuster, UserRole.Observer, UserRole.Operator })
        {
            var user = await SeedUserAsync(role);
            var client = await ClientAsync(user.Login, user.Password);
            var date = NewDate();

            (await client.GetAsync($"/api/shift-plan?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
            (await client.GetAsync("/api/shift-plan/lead-candidates"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
            (await PutLeadAsync(client, date, 1, null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }
    }
}
