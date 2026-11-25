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

    private async Task<string> FirstActiveManagerIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstAsync();
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

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
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
                if (day.GetProperty("shiftDate").GetString() == from.ToString("yyyy-MM-dd"))
                    items.Should().OnlyContain(i => i.GetProperty("canEditOperators").GetBoolean());
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

    [Theory]
    [InlineData(366, HttpStatusCode.OK)]
    [InlineData(367, HttpStatusCode.BadRequest)]
    public async Task Get_RangeLength_IsCappedAt366Days(int days, HttpStatusCode expected)
    {
        var client = await ManagerClientAsync();
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(days - 1);

        var response = await client.GetAsync(
            $"/api/shift-plan?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK)
        {
            (await RequiredJsonAsync(response)).EnumerateArray().Should().HaveCount(days);
        }
        else
        {
            (await JsonAsync(response)).GetProperty("message").GetString().Should().Contain("366");
        }
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
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);

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

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public async Task DstCollapsedWindow_FirstLeadMaterializationPersistsPositiveDuration(int durationMinutes)
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = new DateOnly(2027, 3, 14);
        var client = await ManagerClientAsync();
        var schedule = new List<ShiftScheduleItemDto>
        {
            new()
            {
                StartMinutes = 120,
                DurationMinutes = durationMinutes,
                TimeZoneId = "America/New_York"
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
        }

        try
        {
            await WithScheduleAsync(client, schedule, async () =>
            {
                var day = await RequiredJsonAsync(await PutLeadAsync(client, date, 1, lead.Id));
                var dto = Item(day, 1);
                var start = dto.GetProperty("startAtUtc").GetDateTime();
                var end = dto.GetProperty("endAtUtc").GetDateTime();
                start.Should().BeBefore(end);
                end.Should().Be(start.AddMinutes(durationMinutes));

                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var saved = await db.ShiftPlanItems.SingleAsync(i => i.Day.ShiftDate == date);
                saved.StartAtUtc.Should().BeBefore(saved.EndAtUtc);
                saved.EndAtUtc.Should().Be(saved.StartAtUtc.AddMinutes(durationMinutes));
            });
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
        }
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
            AssignedAt = DateTime.UtcNow,
            CreatedById = oldLead.Id
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
    public async Task SetLead_UnclosedPositionsAllowAssignmentRegardlessOfTiming()
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
        Item(before, 1).GetProperty("canEditLead").GetBoolean().Should().BeTrue();
        Item(before, 2).GetProperty("state").GetString().Should().Be("Current");
        Item(before, 2).GetProperty("canEditLead").GetBoolean().Should().BeTrue(
            "руководитель может назначить старшего в текущую позицию");
        Item(before, 3).GetProperty("canEditLead").GetBoolean().Should().BeTrue(
            "на момент начала текущей позиции руководитель может назначить старшего");
        Item(before, 4).GetProperty("state").GetString().Should().Be("Future");
        Item(before, 4).GetProperty("canEditLead").GetBoolean().Should().BeTrue();

        foreach (var position in new[] { 1, 2, 3, 4 })
            (await PutLeadAsync(client, date, position, lead.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var historicalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        (await PutLeadAsync(client, historicalDate, 1, lead.Id)).StatusCode.Should()
            .Be(HttpStatusCode.OK);
        (await GetDayAsync(client, historicalDate)).GetProperty("isSaved").GetBoolean().Should().BeTrue();
        day.Items.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(ShiftClosureState.Draft, HttpStatusCode.OK, true, true)]
    [InlineData(ShiftClosureState.Closed, HttpStatusCode.BadRequest, false, true)]
    [InlineData(ShiftClosureState.Draft, HttpStatusCode.OK, true, false)]
    [InlineData(ShiftClosureState.Closed, HttpStatusCode.BadRequest, false, false)]
    public async Task SetLead_StartedLeadChangeDependsOnClosureState(
        ShiftClosureState closureState, HttpStatusCode expectedStatus, bool expectedCanEdit, bool ended)
    {
        var oldLead = await SeedUserAsync(UserRole.ShiftLead);
        var newLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var now = DateTime.UtcNow;
        var day = await SeedDayAsync(date, new ShiftPlanItem
        {
            Position = 1,
            StartAtUtc = ended ? now.AddHours(-2) : now.AddHours(-1),
            EndAtUtc = ended ? now.AddHours(-1) : now.AddHours(1),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = oldLead.Id
        });
        var item = day.Items.Single();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = closureState,
                CreatedById = oldLead.Id,
                ClosedById = closureState == ShiftClosureState.Closed ? oldLead.Id : null,
                ClosedAt = closureState == ShiftClosureState.Closed ? now : null
            });
            await db.SaveChangesAsync();
        }
        var client = await ManagerClientAsync();

        Item(await GetDayAsync(client, date), 1).GetProperty("canEditLead").GetBoolean().Should()
            .Be(expectedCanEdit);
        var response = await PutLeadAsync(client, date, 1, newLead.Id);

        response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftPlanItems.SingleAsync(i => i.Id == item.Id)).ShiftLeadId.Should()
                .Be(expectedStatus == HttpStatusCode.OK ? newLead.Id : oldLead.Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_ManagerCanAssignAndReplaceLeadAndReadDraft(bool materialized)
    {
        var managerId = await TestManagerIdAsync();
        var otherManager = await SeedUserAsync(UserRole.Manager);
        var otherLead = await SeedUserAsync(UserRole.ShiftLead);
        var admin = await SeedUserAsync(UserRole.Admin);
        var manager = await ManagerClientAsync();
        var adminClient = await ClientAsync(admin.Login, admin.Password);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        var schedule = new List<ShiftScheduleItemDto>
        {
            new()
            {
                StartMinutes = ((localNow.Hour * 60 + localNow.Minute - 60) % 1440 + 1440) % 1440,
                DurationMinutes = 180,
                TimeZoneId = "Europe/Moscow"
            },
            new()
            {
                StartMinutes = (localNow.Hour * 60 + localNow.Minute + 180) % 1440,
                DurationMinutes = 60,
                TimeZoneId = "Europe/Moscow"
            }
        };
        var today = DateOnly.FromDateTime(localNow)
            .AddDays(schedule[0].StartMinutes > localNow.Hour * 60 + localNow.Minute ? -1 : 0);
        await DeleteDayAsync(today);

        try
        {
            await WithScheduleAsync(manager, schedule, async () =>
            {
                if (materialized)
                {
                    var now = DateTime.UtcNow;
                    await SeedDayAsync(today,
                        new ShiftPlanItem
                        {
                            Position = 1,
                            StartAtUtc = now.AddHours(-1),
                            EndAtUtc = now.AddHours(1),
                            TimeZoneId = "Europe/Moscow"
                        },
                        FutureItem(2));
                }

                var position = 1;
                if (!materialized)
                {
                    var before = await GetDayAsync(manager, today);
                    position = before.GetProperty("items").EnumerateArray()
                        .Single(i => i.GetProperty("state").GetString() == "Current")
                        .GetProperty("position").GetInt32();
                }
                var managerItem = Item(await GetDayAsync(manager, today), position);
                managerItem.GetProperty("canEditLead").GetBoolean().Should().BeTrue(managerItem.GetRawText());
                Item(await GetDayAsync(adminClient, today), position)
                    .GetProperty("canEditLead").GetBoolean().Should().BeTrue();
                (await PutLeadAsync(adminClient, today, position, admin.Id)).StatusCode.Should()
                    .Be(HttpStatusCode.BadRequest);
                if (!materialized)
                    (await GetDayAsync(manager, today)).GetProperty("isSaved").GetBoolean().Should().BeFalse(
                        "отклонённые назначения не материализуют виртуальный день");

                var assigned = await RequiredJsonAsync(await PutLeadAsync(manager, today, position, otherLead.Id));
                assigned.GetProperty("isSaved").GetBoolean().Should().BeTrue();
                assigned.GetProperty("items").EnumerateArray().Should().HaveCount(2,
                    "назначение материализует полный день");
                assigned.GetProperty("items").EnumerateArray().Should()
                    .OnlyContain(i => i.GetProperty("id").ValueKind == JsonValueKind.String);
                Item(assigned, position).GetProperty("shiftLeadId").GetString().Should().Be(otherLead.Id);

                var replaced = await RequiredJsonAsync(
                    await PutLeadAsync(manager, today, position, otherManager.Id));
                Item(replaced, position).GetProperty("shiftLeadId").GetString().Should().Be(otherManager.Id);
                Item(replaced, position).GetProperty("canEditLead").GetBoolean().Should().BeTrue();

                var selfAssigned = await RequiredJsonAsync(await PutLeadAsync(manager, today, position, managerId));
                var item = Item(selfAssigned, position);
                item.GetProperty("shiftLeadId").GetString().Should().Be(managerId);
                item.GetProperty("closureStatus").GetString().Should().Be("Draft");
                item.GetProperty("canEditLead").GetBoolean().Should().BeTrue();
                var itemId = item.GetProperty("id").GetGuid();

                var closure = await RequiredJsonAsync(
                    await manager.GetAsync($"/api/shift-closures/items/{itemId}"));
                closure.GetProperty("shiftPlanItemId").GetGuid().Should().Be(itemId);
                closure.GetProperty("state").GetString().Should().Be("Draft");
            });
        }
        finally
        {
            await DeleteDayAsync(today);
        }

        var endedDate = NewDate();
        var nowEnded = DateTime.UtcNow;
        await SeedDayAsync(endedDate, new ShiftPlanItem
        {
            Position = 1,
            StartAtUtc = nowEnded.AddHours(-2),
            EndAtUtc = nowEnded.AddHours(-1),
            TimeZoneId = "Europe/Moscow"
        });
        (await PutLeadAsync(manager, endedDate, 1, managerId)).StatusCode.Should()
            .Be(HttpStatusCode.OK, "руководитель может назначить себя старшим завершённой незакрытой смены");
    }

    [Theory]
    [InlineData(ShiftClosureState.Draft)]
    [InlineData(ShiftClosureState.Closed)]
    public async Task ClearLead_WithExistingClosure_IsRejectedAndLastEmptyDaySurvives(ShiftClosureState state)
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var day = await SeedDayAsync(date, FutureItem(1, lead.Id));
        var item = day.Items.Single();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = state,
                CreatedById = lead.Id,
                ClosedById = state == ShiftClosureState.Closed ? lead.Id : null,
                ClosedAt = state == ShiftClosureState.Closed ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
        }
        var client = await ManagerClientAsync();
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);

        var response = await PutLeadAsync(client, date, 1, null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var persisted = await db.ShiftPlanItems.AsNoTracking()
                .Include(i => i.Closure)
                .SingleAsync(i => i.Id == item.Id);
            persisted.ShiftLeadId.Should().Be(lead.Id);
            persisted.Closure.Should().NotBeNull();
            (await db.ShiftPlanDays.CountAsync(d => d.Id == day.Id)).Should().Be(1);
        }
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
            UnassignedAt = DateTime.UtcNow.AddHours(-1),
            CreatedById = lead1.Id
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }
        var client = await ManagerClientAsync();
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);

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
                var advisoryWaiting = 0;
                for (var attempt = 0; attempt < 100 && (blocked < 1 || advisoryWaiting < 1); attempt++)
                {
                    await using var command = new NpgsqlCommand("""
                        SELECT
                          (SELECT count(*) FROM pg_locks
                           WHERE relation = '"ShiftPlanDays"'::regclass
                             AND mode = 'RowExclusiveLock' AND NOT granted),
                          (SELECT count(*) FROM pg_locks
                           WHERE locktype = 'advisory' AND NOT granted)
                        """, connection, transaction);
                    await using var reader = await command.ExecuteReaderAsync();
                    (await reader.ReadAsync()).Should().BeTrue();
                    blocked = reader.GetInt32(0);
                    advisoryWaiting = reader.GetInt32(1);
                    if (blocked < 1 || advisoryWaiting < 1) await Task.Delay(50);
                }
                blocked.Should().Be(1, "первый запрос должен ожидать материализацию виртуального дня");
                advisoryWaiting.Should().Be(1, "второй запрос должен ожидать общую блокировку графика");
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
    public async Task ConcurrentExistingDayLeadChanges_ReturnsOneSuccessAndOne409WithoutOverwrite()
    {
        var originalLead = await SeedUserAsync(UserRole.ShiftLead);
        var managerLead = await SeedUserAsync(UserRole.ShiftLead);
        var adminLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        await SeedDayAsync(date, FutureItem(1, originalLead.Id));
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
            "LOCK TABLE \"ShiftPlanItems\" IN EXCLUSIVE MODE", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync();

        // EXCLUSIVE разрешает обоим запросам прочитать исходного старшего A,
        // но задерживает UPDATE первого и переводит второй на advisory lock.
        var managerRequest = PutLeadAsync(manager, date, 1, managerLead.Id);
        var adminRequest = PutLeadAsync(admin, date, 1, adminLead.Id);
        var blocked = 0;
        var advisoryWaiting = 0;
        for (var attempt = 0; attempt < 200 && (blocked < 1 || advisoryWaiting < 1); attempt++)
        {
            await using var command = new NpgsqlCommand("""
                SELECT
                  (SELECT count(*) FROM pg_locks
                   WHERE relation = '"ShiftPlanItems"'::regclass
                     AND mode = 'RowExclusiveLock' AND NOT granted),
                  (SELECT count(*) FROM pg_locks
                   WHERE locktype = 'advisory' AND NOT granted)
                """, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            blocked = reader.GetInt32(0);
            advisoryWaiting = reader.GetInt32(1);
            if (blocked < 1 || advisoryWaiting < 1) await Task.Delay(50);
        }
        blocked.Should().Be(1, "первый запрос должен дойти до изменения уже существующей позиции");
        advisoryWaiting.Should().Be(1, "второй запрос должен прочитать A и ожидать первый");

        await transaction.CommitAsync();
        var responses = await Task.WhenAll(managerRequest, adminRequest);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await JsonAsync(conflict)).GetProperty("message").GetString().Should()
            .Be("План смены уже изменён другим пользователем");

        var committedLeadId = responses[0].StatusCode == HttpStatusCode.OK
            ? managerLead.Id
            : adminLead.Id;
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await verifyDb.ShiftPlanItems.SingleAsync(i => i.Day.ShiftDate == date && i.Position == 1))
            .ShiftLeadId.Should().Be(committedLeadId, "проигравший запрос не должен перезаписать победителя");
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
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);

        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // EXCLUSIVE пропускает pre-lock snapshot SELECT, но задерживает DELETE/UPDATE:
        // оба запроса видят исходный день до сериализации на advisory lock.
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE \"ShiftPlanItems\" IN EXCLUSIVE MODE", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync();

        var clearRequest = PutLeadAsync(manager, date, 1, null);
        var reassignRequest = Task.FromResult<HttpResponseMessage>(null!);
        var committed = false;
        try
        {
            await WaitForBlockedLockAsync("ShiftPlanItems", "relation");
            reassignRequest = PutLeadAsync(admin, date, 1, newLead.Id);
            await WaitForReassignLockAsync();
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

        async Task WaitForReassignLockAsync()
        {
            var locked = 0;
            var advisoryWaiting = 0;
            for (var attempt = 0; attempt < 100 && (locked < 1 || advisoryWaiting < 1); attempt++)
            {
                await using var command = new NpgsqlCommand("""
                    SELECT
                      (SELECT count(*) FROM pg_locks
                       WHERE relation = '"ShiftPlanDays"'::regclass
                         AND locktype = 'relation' AND mode = 'RowShareLock' AND granted),
                      (SELECT count(*) FROM pg_locks
                       WHERE locktype = 'advisory' AND NOT granted)
                    """, connection, transaction);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                locked = reader.GetInt32(0);
                advisoryWaiting = reader.GetInt32(1);
                if (locked < 1 || advisoryWaiting < 1) await Task.Delay(50);
            }
            locked.Should().Be(1, "очистка должна удерживать сохранённый день");
            advisoryWaiting.Should().Be(1, "переназначение должно ожидать общую блокировку графика");
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
        const int position = 99;
        var nearest = await SeedDayAsync(nearestDate, new ShiftPlanItem
        {
            Position = position,
            StartAtUtc = now.AddHours(-1),
            EndAtUtc = now.AddHours(1),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = lead.Id
        });
        var later = await SeedDayAsync(laterDate, new ShiftPlanItem
        {
            Position = position,
            StartAtUtc = now.AddHours(2),
            EndAtUtc = now.AddHours(3),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = lead.Id
        });
        var client = await ManagerClientAsync();

        var laterOnly = await GetDayAsync(client, laterDate);
        Item(laterOnly, position).GetProperty("canEditOperators").GetBoolean().Should().BeFalse(
            "текущая смена той же позиции находится вне фильтра, но она ближайшая");
        var nearestOnly = await GetDayAsync(client, nearestDate);
        Item(nearestOnly, position).GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

        nearest.Items.Single().Id.Should().NotBe(later.Items.Single().Id);
    }

    [Fact]
    public async Task Get_ResolvesEffectiveLeadForVirtualAndSavedDays()
    {
        var defaultLead = await SeedUserAsync(UserRole.ShiftLead);
        var explicitLead = await SeedUserAsync(UserRole.ShiftLead);
        var managerId = await FirstActiveManagerIdAsync();
        var date = NewDate();
        var client = await ManagerClientAsync();

        await WithScheduleAsync(client, TwoShifts(), async () =>
        {
            try
            {
                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                    await db.ShiftDefaultLeads.Where(d => d.Position == 1).ExecuteDeleteAsync();
                    db.ShiftDefaultLeads.Add(new ShiftDefaultLead { Position = 1, ShiftLeadId = defaultLead.Id });
                    await db.SaveChangesAsync();
                }

                var virtualDay = await GetDayAsync(client, date);
                var virtualDefault = Item(virtualDay, 1);
                virtualDefault.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
                virtualDefault.GetProperty("shiftLeadId").GetString().Should().Be(defaultLead.Id);
                virtualDefault.GetProperty("shiftLeadName").GetString().Should().Be(defaultLead.FullName);
                virtualDefault.GetProperty("shiftLeadSource").GetString().Should().Be("Default");
                virtualDefault.GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeFalse();
                virtualDefault.GetProperty("canEditOperators").GetBoolean().Should().BeFalse(
                    "виртуальная позиция до Task 4 остаётся недоступной для изменения литейщиков");
                var virtualManager = Item(virtualDay, 2);
                virtualManager.GetProperty("shiftLeadId").GetString().Should().Be(managerId);
                virtualManager.GetProperty("shiftLeadName").GetString().Should().NotBeNullOrWhiteSpace();
                virtualManager.GetProperty("shiftLeadSource").GetString().Should().Be("ManagerFallback");
                virtualManager.GetProperty("hasMissingLeadWarning").GetBoolean().Should().BeFalse();
                virtualManager.GetProperty("canEditOperators").GetBoolean().Should().BeFalse();

                var savedDay = await RequiredJsonAsync(await PutLeadAsync(client, date, 2, explicitLead.Id));
                savedDay.GetProperty("isSaved").GetBoolean().Should().BeTrue();
                Item(savedDay, 1).GetProperty("shiftLeadId").GetString().Should().Be(defaultLead.Id);
                Item(savedDay, 1).GetProperty("shiftLeadSource").GetString().Should().Be("Default");
                var explicitItem = Item(savedDay, 2);
                explicitItem.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
                explicitItem.GetProperty("shiftLeadId").GetString().Should().Be(explicitLead.Id);
                explicitItem.GetProperty("shiftLeadName").GetString().Should().Be(explicitLead.FullName);
                explicitItem.GetProperty("shiftLeadSource").GetString().Should().Be("Explicit");
            }
            finally
            {
                await DeleteDayAsync(date);
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                await db.ShiftDefaultLeads.Where(d => d.Position == 1).ExecuteDeleteAsync();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClearLead_WithFallback_PreservesClosureAndAssignments(bool useDefault)
    {
        var explicitLead = await SeedUserAsync(UserRole.ShiftLead);
        var defaultLead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var managerId = await FirstActiveManagerIdAsync();
        var date = NewDate();
        var day = await SeedDayAsync(date, FutureItem(1, explicitLead.Id));
        var item = day.Items.Single();
        var client = await ManagerClientAsync();

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                await db.ShiftDefaultLeads.Where(d => d.Position == 1).ExecuteDeleteAsync();
                if (useDefault)
                    db.ShiftDefaultLeads.Add(new ShiftDefaultLead { Position = 1, ShiftLeadId = defaultLead.Id });
                db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
                {
                    ShiftPlanItemId = item.Id,
                    ImmId = _factory.TestImmId,
                    OperatorId = @operator.Id,
                    Slot = 1,
                    AssignedAt = item.StartAtUtc,
                    CreatedById = explicitLead.Id
                });
                db.ShiftClosures.Add(new ShiftClosure
                {
                    ShiftPlanItemId = item.Id,
                    State = ShiftClosureState.Draft,
                    CreatedById = explicitLead.Id
                });
                await db.SaveChangesAsync();
            }

            var cleared = await RequiredJsonAsync(await PutLeadAsync(client, date, 1, null));
            var effectiveLeadId = useDefault ? defaultLead.Id : managerId;
            var effectiveSource = useDefault ? "Default" : "ManagerFallback";
            cleared.GetProperty("isSaved").GetBoolean().Should().BeTrue();
            var clearedItem = Item(cleared, 1);
            clearedItem.GetProperty("shiftLeadId").GetString().Should().Be(effectiveLeadId);
            clearedItem.GetProperty("shiftLeadSource").GetString().Should().Be(effectiveSource);
            clearedItem.GetProperty("operatorAssignmentCount").GetInt32().Should().Be(1);
            clearedItem.GetProperty("closureStatus").GetString().Should().Be("Draft");

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var persisted = await db.ShiftPlanItems.AsNoTracking()
                    .Include(i => i.Closure)
                    .Include(i => i.OperatorAssignments)
                    .SingleAsync(i => i.Id == item.Id);
                persisted.ShiftLeadId.Should().BeNull("очищается только явный override");
                persisted.Closure.Should().NotBeNull();
                persisted.OperatorAssignments.Should().ContainSingle();
            }
        }
        finally
        {
            await DeleteDayAsync(date);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            await db.ShiftDefaultLeads.Where(d => d.Position == 1).ExecuteDeleteAsync();
        }
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

internal sealed class NoLeadFallbackScope : IAsyncDisposable
{
    private readonly IntegrationTestFactory _factory;
    private readonly IReadOnlyCollection<string> _managerIds;
    private readonly IReadOnlyCollection<ShiftDefaultLead> _defaults;

    private NoLeadFallbackScope(
        IntegrationTestFactory factory,
        IReadOnlyCollection<string> managerIds,
        IReadOnlyCollection<ShiftDefaultLead> defaults)
    {
        _factory = factory;
        _managerIds = managerIds;
        _defaults = defaults;
    }

    public static async Task<NoLeadFallbackScope> CreateAsync(IntegrationTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var managerIds = await db.Users
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .Select(u => u.Id)
            .ToListAsync();
        var defaults = await db.ShiftDefaultLeads.AsNoTracking()
            .Select(d => new ShiftDefaultLead { Position = d.Position, ShiftLeadId = d.ShiftLeadId })
            .ToListAsync();
        await db.ShiftDefaultLeads.ExecuteDeleteAsync();
        await db.Users.Where(u => managerIds.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        return new NoLeadFallbackScope(factory, managerIds, defaults);
    }

    public async ValueTask DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Users.Where(u => _managerIds.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, true));
        if (_defaults.Count > 0)
        {
            db.ShiftDefaultLeads.AddRange(_defaults);
            await db.SaveChangesAsync();
        }
    }
}
