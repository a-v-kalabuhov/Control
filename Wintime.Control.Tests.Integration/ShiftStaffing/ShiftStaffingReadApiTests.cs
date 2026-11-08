using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.ShiftStaffing;

[Collection("Integration")]
public class ShiftStaffingReadApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 1000;

    private readonly IntegrationTestFactory _factory;

    public ShiftStaffingReadApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(
        string Id, string Login, string Password, string FullName, string? EmployeeId);

    [Fact]
    public async Task Nearest_PrefersCurrentThenEarliestFuture_AndReturnsNullWithoutOwnCurrentOrFuture()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var day = await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(2), now.AddHours(3)),
            Item(2, lead.Id, now.AddHours(-1), now.AddHours(1)),
            Item(3, lead.Id, now.AddHours(4), now.AddHours(5)),
            Item(4, other.Id, now.AddHours(-2), now.AddHours(2)),
            Item(5, lead.Id, now.AddHours(-5), now.AddHours(-4)));
        var current = day.Items.Single(i => i.Position == 2);
        var earliestFuture = day.Items.Single(i => i.Position == 1);
        var client = await ClientAsync(lead);

        var currentBody = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/nearest"));
        currentBody.GetProperty("item").GetProperty("itemId").GetGuid().Should().Be(current.Id);
        currentBody.GetProperty("item").GetProperty("state").GetString().Should().Be("Current");
        currentBody.GetProperty("item").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var tracked = await db.ShiftPlanItems.SingleAsync(i => i.Id == current.Id);
            tracked.StartAtUtc = now.AddHours(-4);
            tracked.EndAtUtc = now.AddHours(-3);
            await db.SaveChangesAsync();
        }

        var futureBody = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/nearest"));
        futureBody.GetProperty("item").GetProperty("itemId").GetGuid().Should().Be(earliestFuture.Id);
        futureBody.GetProperty("item").GetProperty("state").GetString().Should().Be("Future");

        var historicalOnlyLead = await SeedUserAsync(UserRole.ShiftLead);
        await SeedDayAsync(
            Item(1, historicalOnlyLead.Id, now.AddHours(-3), now.AddHours(-2)),
            Item(2, other.Id, now.AddHours(1), now.AddHours(2)));
        var historicalOnlyClient = await ClientAsync(historicalOnlyLead);
        var emptyBody = await RequiredJsonAsync(
            await historicalOnlyClient.GetAsync("/api/shift-staffing/nearest"));
        emptyBody.GetProperty("item").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task History_ReturnsOnlyOwnHistoricalItemsOrderedByStartDescending()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var olderDay = await SeedDayAsync(Item(1, lead.Id, now.AddHours(-8), now.AddHours(-7)));
        var newerDay = await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-4), now.AddHours(-3)),
            Item(2, lead.Id, now.AddHours(2), now.AddHours(3)),
            Item(3, other.Id, now.AddHours(-2), now.AddHours(-1)));
        var older = olderDay.Items.Single();
        var newer = newerDay.Items.Single(i => i.Position == 1);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/history"));
        var items = body.EnumerateArray().ToList();

        items.Select(i => i.GetProperty("itemId").GetGuid()).Should().Equal(newer.Id, older.Id);
        items.Select(i => i.GetProperty("startAtUtc").GetDateTime()).Should().BeInDescendingOrder();
        items.Should().OnlyContain(i => i.GetProperty("state").GetString() == "Historical");
        items.Should().OnlyContain(i => !i.GetProperty("canEditOperators").GetBoolean());
    }

    [Fact]
    public async Task ItemAccess_ShiftLeadGetsNearestAndOwnHistoryOnly_ManagerAdminGetAnyWithServerEditFlags()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var nearestDay = await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)),
            Item(2, lead.Id, now.AddHours(-1), now.AddHours(1)));
        var nearest = nearestDay.Items.Single(i => i.Position == 1);
        var otherPosition = nearestDay.Items.Single(i => i.Position == 2);
        var later = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(2), now.AddHours(3)))).Items.Single();
        var historical = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-4), now.AddHours(-3)))).Items.Single();
        var foreign = (await SeedDayAsync(Item(3, other.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var leadClient = await ClientAsync(lead);

        var nearestBody = await RequiredJsonAsync(
            await leadClient.GetAsync($"/api/shift-staffing/items/{nearest.Id}"));
        nearestBody.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
        nearestBody.GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
        nearestBody.GetProperty("shiftLeadName").GetString().Should().Be(lead.FullName);

        var historicalBody = await RequiredJsonAsync(
            await leadClient.GetAsync($"/api/shift-staffing/items/{historical.Id}"));
        historicalBody.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeFalse();

        (await leadClient.GetAsync($"/api/shift-staffing/items/{later.Id}")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await leadClient.GetAsync($"/api/shift-staffing/items/{foreign.Id}")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await leadClient.GetAsync($"/api/shift-staffing/items/{otherPosition.Id}")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden, "для старшего доступна только его ближайшая позиция");
        (await leadClient.GetAsync($"/api/shift-staffing/items/{Guid.NewGuid()}")).StatusCode.Should()
            .Be(HttpStatusCode.NotFound);

        foreach (var client in new[] { await ManagerClientAsync(), await AdminClientAsync() })
        {
            var nearestForManager = await RequiredJsonAsync(
                await client.GetAsync($"/api/shift-staffing/items/{nearest.Id}"));
            nearestForManager.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

            var laterForManager = await RequiredJsonAsync(
                await client.GetAsync($"/api/shift-staffing/items/{later.Id}"));
            laterForManager.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeFalse();

            var otherPositionForManager = await RequiredJsonAsync(
                await client.GetAsync($"/api/shift-staffing/items/{otherPosition.Id}"));
            otherPositionForManager.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

            (await client.GetAsync($"/api/shift-staffing/items/{historical.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync($"/api/shift-staffing/items/{foreign.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task CurrentItem_ContainsEveryActiveImmIncludingNoTask_AndExcludesArchivedImm()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var activeNoTask = await SeedImmAsync(isActive: true);
        var archived = await SeedImmAsync(isActive: false);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        using (var scope = _factory.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<IImmStatusCache>();
            cache.SetStatus(activeNoTask, ImmStatus.Manual, now.AddMinutes(-1));
        }
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var imms = body.GetProperty("imms").EnumerateArray().ToList();

        imms.Select(i => i.GetProperty("immId").GetGuid()).Should().Contain(activeNoTask);
        imms.Select(i => i.GetProperty("immId").GetGuid()).Should().NotContain(archived);
        var noTask = imms.Single(i => i.GetProperty("immId").GetGuid() == activeNoTask);
        noTask.GetProperty("isActive").GetBoolean().Should().BeTrue();
        noTask.GetProperty("tasks").EnumerateArray().Should().BeEmpty();
        noTask.GetProperty("assignments").EnumerateArray().Should().BeEmpty();
        noTask.GetProperty("effectiveStatus").GetString().Should().Be(EffectiveStatus.NoTask);
        noTask.GetProperty("activeOperatorCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task HistoricalItem_IncludesArchivedOnlyWithTaskOrAssignment_AndMapsAllOperatorIntervalsAndStatus()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-6);
        var end = now.AddHours(-4);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var activeOperator = await SeedUserAsync(UserRole.Operator);
        var archivedOperator = await SeedUserAsync(UserRole.Operator, isActive: false, withLogin: false);
        var archivedWithTask = await SeedImmAsync(isActive: false);
        var archivedWithAssignment = await SeedImmAsync(isActive: false);
        var archivedBare = await SeedImmAsync(isActive: false);
        var activeNoTask = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(archivedWithTask, TaskStatus.Completed,
            plannedStartAt: start.AddDays(2), setupStartedAt: start.AddHours(-1),
            startedAt: start.AddHours(-1), completedAt: end);
        var firstAssignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = archivedWithAssignment,
            OperatorId = activeOperator.Id,
            Slot = 1,
            AssignedAt = start.AddHours(-1),
            UnassignedAt = start.AddHours(1)
        };
        var secondAssignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = archivedWithAssignment,
            OperatorId = archivedOperator.Id,
            Slot = 2,
            AssignedAt = end.AddHours(1)
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.AddRange(firstAssignment, secondAssignment);
            db.ImmStatusHistory.Add(new ImmStatusHistory
            {
                ImmId = archivedWithTask,
                Status = ImmStatus.Auto,
                ChangedAt = start.AddHours(-2)
            });
            await db.SaveChangesAsync();
        }
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var imms = body.GetProperty("imms").EnumerateArray().ToList();
        var ids = imms.Select(i => i.GetProperty("immId").GetGuid()).ToList();
        ids.Should().Contain(new[] { archivedWithTask, archivedWithAssignment, activeNoTask });
        ids.Should().NotContain(archivedBare);

        var withTask = imms.Single(i => i.GetProperty("immId").GetGuid() == archivedWithTask);
        withTask.GetProperty("tasks").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("id").GetGuid().Should().Be(task.Id);
        withTask.GetProperty("effectiveStatus").GetString().Should().Be(EffectiveStatus.Production);

        var withAssignments = imms.Single(i => i.GetProperty("immId").GetGuid() == archivedWithAssignment);
        var assignments = withAssignments.GetProperty("assignments").EnumerateArray().ToList();
        assignments.Select(a => a.GetProperty("id").GetGuid()).Should()
            .Equal(firstAssignment.Id, secondAssignment.Id);
        assignments[0].GetProperty("operatorName").GetString().Should().Be(activeOperator.FullName);
        assignments[0].GetProperty("workStart").GetDateTime().Should().BeCloseTo(start, TimeSpan.FromSeconds(1));
        assignments[0].GetProperty("workEnd").GetDateTime().Should()
            .BeCloseTo(start.AddHours(1), TimeSpan.FromSeconds(1));
        assignments[0].GetProperty("isWorkIntervalEmpty").GetBoolean().Should().BeFalse();
        assignments[1].GetProperty("operatorName").GetString().Should().Be(archivedOperator.FullName);
        assignments[1].GetProperty("operatorEmployeeId").GetString().Should().Be(archivedOperator.EmployeeId);
        assignments[1].GetProperty("workStart").GetDateTime().Should()
            .BeCloseTo(end.AddHours(1), TimeSpan.FromSeconds(1));
        assignments[1].GetProperty("workEnd").GetDateTime().Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
        assignments[1].GetProperty("isWorkIntervalEmpty").GetBoolean().Should().BeTrue();
        withAssignments.GetProperty("activeOperatorCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ItemTasks_ApplyPlannedAndActivityRulesWithoutDuplicates_AndUseFullTaskDtoMapping()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-2);
        var end = now.AddHours(2);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var planned = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(10));
        var issued = await SeedTaskAsync(immId, TaskStatus.Issued, start.AddMinutes(20));
        var plannedAtEnd = await SeedTaskAsync(immId, TaskStatus.Planned, end);
        var plannedBefore = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddSeconds(-1));
        var setup = await SeedTaskAsync(immId, TaskStatus.Setup, end.AddDays(2),
            setupStartedAt: start.AddHours(-1));
        var inProgress = await SeedTaskAsync(immId, TaskStatus.InProgress, start.AddMinutes(30),
            setupStartedAt: start.AddHours(-1), startedAt: start.AddMinutes(-30));
        var completed = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(2),
            setupStartedAt: start.AddMinutes(30), startedAt: start.AddMinutes(40),
            completedAt: start.AddHours(1));
        var completedBefore = await SeedTaskAsync(immId, TaskStatus.Completed, start,
            setupStartedAt: start.AddHours(-3), startedAt: start.AddHours(-2),
            completedAt: start.AddHours(-1));
        var closedAtStart = await SeedTaskAsync(immId, TaskStatus.Closed, start,
            setupStartedAt: start.AddHours(-2), startedAt: start.AddHours(-1), closedAt: start);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var tasks = Imm(body, immId).GetProperty("tasks").EnumerateArray().ToList();
        var ids = tasks.Select(t => t.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(new[] { planned.Id, issued.Id, setup.Id, inProgress.Id, completed.Id });
        ids.Should().NotContain(new[] { plannedAtEnd.Id, plannedBefore.Id, completedBefore.Id, closedAtStart.Id });
        ids.Should().OnlyHaveUniqueItems();
        ids.Count(i => i == inProgress.Id).Should().Be(1,
            "задание одновременно попадает в широкий planned и activity запросы");
        var dto = tasks.Single(t => t.GetProperty("id").GetGuid() == planned.Id);
        dto.GetProperty("immName").GetString().Should().NotBeNullOrWhiteSpace();
        dto.GetProperty("moldName").GetString().Should().Be("Test Mold");
        dto.GetProperty("productFullArticle").GetString().Should().Be("TYPE-TEST.0");
        dto.GetProperty("plannedStartAt").GetDateTime().Should()
            .BeCloseTo(start.AddMinutes(10), TimeSpan.FromSeconds(1));
        dto.GetProperty("plannedReleaseSeconds").GetInt64().Should().Be(600);
        dto.GetProperty("plannedEndAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task ItemTasks_MultiShiftActivityAppearsInEveryIntersectedOwnHistoricalShift()
    {
        var now = DateTime.UtcNow;
        var firstStart = now.AddHours(-12);
        var firstEnd = now.AddHours(-10);
        var secondStart = now.AddHours(-8);
        var secondEnd = now.AddHours(-6);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync(isActive: true);
        var first = (await SeedDayAsync(Item(1, lead.Id, firstStart, firstEnd))).Items.Single();
        var second = (await SeedDayAsync(Item(1, lead.Id, secondStart, secondEnd))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, firstStart.AddDays(2),
            setupStartedAt: firstStart.AddHours(-1), startedAt: firstStart.AddHours(-1),
            completedAt: secondEnd.AddMinutes(-30));
        var client = await ClientAsync(lead);

        var firstBody = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{first.Id}"));
        var secondBody = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{second.Id}"));

        Imm(firstBody, immId).GetProperty("tasks").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("id").GetGuid().Should().Be(task.Id);
        Imm(secondBody, immId).GetProperty("tasks").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("id").GetGuid().Should().Be(task.Id);
    }

    [Fact]
    public async Task Operators_ReturnOnlyActiveOperatorsIncludingWithoutLogin_WithRestrictedDtoShape()
    {
        var withLogin = await SeedUserAsync(UserRole.Operator);
        var withoutLogin = await SeedUserAsync(UserRole.Operator, withLogin: false);
        var archived = await SeedUserAsync(UserRole.Operator, isActive: false);
        var adjuster = await SeedUserAsync(UserRole.Adjuster);
        var client = await ManagerClientAsync();

        var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/operators"));
        var operators = body.EnumerateArray().ToList();
        var ids = operators.Select(o => o.GetProperty("id").GetString()).ToList();

        ids.Should().Contain(new[] { withLogin.Id, withoutLogin.Id });
        ids.Should().NotContain(new[] { archived.Id, adjuster.Id });
        operators.Should().OnlyContain(o => o.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
            .SequenceEqual(new[] { "employeeId", "fullName", "id" }));
        operators.Single(o => o.GetProperty("id").GetString() == withoutLogin.Id)
            .GetProperty("fullName").GetString().Should().Be(withoutLogin.FullName);
    }

    [Fact]
    public async Task Authorization_ServerOnlyNearestAndHistory_ReadPolicyAndNoShiftLeadLegacyAccess()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var adjuster = await SeedUserAsync(UserRole.Adjuster);
        var observer = await SeedUserAsync(UserRole.Observer);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var manager = await SeedUserAsync(UserRole.Manager);
        var admin = await SeedUserAsync(UserRole.Admin);
        var leadClient = await ClientAsync(lead);

        (await leadClient.GetAsync("/api/shift-staffing/nearest")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await leadClient.GetAsync("/api/shift-staffing/history")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await leadClient.GetAsync("/api/shift-staffing/operators")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await leadClient.GetAsync($"/api/shift-staffing/items/{Guid.NewGuid()}")).StatusCode.Should()
            .Be(HttpStatusCode.NotFound);

        foreach (var user in new[] { manager, admin, adjuster, observer, @operator })
        {
            var client = await ClientAsync(user);
            (await client.GetAsync("/api/shift-staffing/nearest")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync("/api/shift-staffing/history")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        foreach (var user in new[] { adjuster, observer, @operator })
        {
            var client = await ClientAsync(user);
            (await client.GetAsync("/api/shift-staffing/operators")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync($"/api/shift-staffing/items/{Guid.NewGuid()}")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
        }

        (await (await ManagerClientAsync()).GetAsync("/api/shift-staffing/operators")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await AdminClientAsync()).GetAsync("/api/shift-staffing/operators")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var url in new[]
                 {
                     "/api/tasks",
                     $"/api/tasks/{Guid.NewGuid()}",
                     "/api/personnel",
                     $"/api/imm/{_factory.TestImmId}/effective-status-history?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z"
                 })
        {
            (await leadClient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }
    }

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
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerClientAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdminClientAsync() => ClientAsync("admin", "Admin123!");

    private async Task<TestUser> SeedUserAsync(
        UserRole role, bool isActive = true, bool withLogin = true)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = withLogin ? $"staffing_{role}_{suffix}" : string.Empty;
        const string password = "Passw0rd!";
        var fullName = $"{role} {suffix}";
        var employeeId = $"EMP-{suffix}"[..12];
        var user = new User
        {
            UserName = withLogin ? login : null,
            NormalizedUserName = withLogin ? login.ToUpperInvariant() : null,
            Email = withLogin ? $"{login}@test.local" : null,
            NormalizedEmail = withLogin ? $"{login.ToUpperInvariant()}@TEST.LOCAL" : null,
            FullName = fullName,
            EmployeeId = employeeId,
            Role = role,
            IsActive = isActive,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        using var scope = _factory.Services.CreateScope();
        if (withLogin)
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        }
        else
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        return new TestUser(user.Id, login, password, fullName, employeeId);
    }

    private async Task<Guid> SeedImmAsync(bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"STAFFING-{Guid.NewGuid():N}",
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
        DateTime? closedAt = null)
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
            ClosedAt = Utc(closedAt),
            IssuedAt = status == TaskStatus.Issued ? DateTime.UtcNow : null
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static DateTime? Utc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static JsonElement Imm(JsonElement item, Guid immId) => item.GetProperty("imms")
        .EnumerateArray().Should().ContainSingle(i => i.GetProperty("immId").GetGuid() == immId).Subject;
}
