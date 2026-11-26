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

namespace Wintime.Control.Tests.Integration.ShiftClosures;

[Collection("Integration")]
public class ShiftClosureReadApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 5000;
    private static int _positionOffset = 32000;
    private readonly IntegrationTestFactory _factory;

    public ShiftClosureReadApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(
        string Id, string Login, string Password, string FullName, string? EmployeeId);

    private sealed record LeadIdentity(string Id, string FullName);

    [Fact]
    public async Task Draft_ComputesPlannedIssuedCompletedClosedAndEarlyWindowWithoutPersistedClosure()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-2));
        var end = Utc(now.AddHours(2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var planned = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(10),
            planQuantity: 12, cavities: 2, partWeight: 5, fullCycleSeconds: 60, packagingUnit: 20);
        var issued = await SeedTaskAsync(immId, TaskStatus.Issued, start.AddMinutes(20),
            planQuantity: 12, cavities: 2, partWeight: 5, fullCycleSeconds: 60);
        var completed = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            planQuantity: 12, cavities: 2, partWeight: 5, fullCycleSeconds: 60,
            setupStartedAt: start.AddMinutes(30), startedAt: start.AddMinutes(35),
            completedAt: start.AddHours(1));
        var closed = await SeedTaskAsync(immId, TaskStatus.Closed, end.AddDays(1),
            planQuantity: 12, cavities: 2, partWeight: 5, fullCycleSeconds: 60,
            setupStartedAt: start.AddMinutes(40), startedAt: start.AddMinutes(45),
            closedAt: start.AddHours(1));
        await SeedCycleAsync(completed, start.AddMinutes(-10), cavities: 2, durationSeconds: 50);
        await SeedCycleAsync(completed, start.AddMinutes(40), cavities: 2, durationSeconds: 40);
        await SeedCycleAsync(completed, start.AddMinutes(50), cavities: 0, durationSeconds: 60);
        await SeedCycleAsync(completed, start.AddMinutes(55), cavities: 100, durationSeconds: 10,
            isSuccessful: false);
        await SeedCycleAsync(completed, start.AddMinutes(56), cavities: 100, durationSeconds: 10,
            open: true);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        body.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("shiftPlanItemId").GetGuid().Should().Be(item.Id);
        body.GetProperty("state").GetString().Should().Be("Draft");
        body.GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
        body.GetProperty("shiftLeadName").GetString().Should().Be(lead.FullName);
        body.GetProperty("factEndUtc").GetDateTime().Should().BeBefore(end);
        body.GetProperty("closedEarly").GetBoolean().Should().BeFalse();
        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("taskId").GetGuid()).Should()
            .BeEquivalentTo(new[] { planned.Id, issued.Id, completed.Id, closed.Id });

        var plannedEntry = Entry(body, planned.Id);
        plannedEntry.GetProperty("immName").GetString().Should().NotBeNullOrWhiteSpace();
        plannedEntry.GetProperty("productName").GetString().Should().Be("По умолчанию");
        plannedEntry.GetProperty("remainingAtStart").GetInt32().Should().Be(12);
        plannedEntry.GetProperty("normPartWeightGrams").GetDecimal().Should().Be(5);
        plannedEntry.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(60);
        plannedEntry.GetProperty("taskPackagingUnit").GetInt32().Should().Be(20);
        plannedEntry.GetProperty("cavities").GetInt32().Should().Be(2);
        plannedEntry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(12,
            "досрочное фактическое окно не уменьшает полный план смены");
        plannedEntry.GetProperty("plannedMaterialWeightGrams").GetDecimal().Should().Be(60);
        plannedEntry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(0);
        plannedEntry.GetProperty("automaticAverageFullCycleSeconds").ValueKind.Should()
            .Be(JsonValueKind.Null);
        Entry(body, issued.Id).GetProperty("shiftPlanQuantity").GetInt32().Should().Be(12);
        Entry(body, closed.Id).GetProperty("shiftPlanQuantity").GetInt32().Should().Be(12);

        var completedEntry = Entry(body, completed.Id);
        completedEntry.GetProperty("remainingAtStart").GetInt32().Should().Be(10);
        completedEntry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(10);
        completedEntry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(3,
            "нулевой Cavities использует Mold.Cavities; открытый и неуспешный циклы исключены");
        completedEntry.GetProperty("automaticAverageFullCycleSeconds").GetDouble().Should().Be(50);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosures.AnyAsync(c => c.ShiftPlanItemId == item.Id)).Should().BeFalse(
            "динамическое чтение не создаёт черновик");
    }

    [Fact]
    public async Task Draft_ReflectsTaskChangesAndJoinsManualValuesWithoutDeletingDisappearedRows()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-1));
        var end = Utc(now.AddHours(2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var stable = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(10));
        var cancelled = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(20));
        var startedClosed = await SeedTaskAsync(immId, TaskStatus.Closed, end.AddDays(1),
            setupStartedAt: start.AddMinutes(15), startedAt: start.AddMinutes(20),
            closedAt: start.AddMinutes(50));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = ShiftClosureState.Draft,
                CreatedById = lead.Id,
                Entries =
                {
                    new ShiftClosureEntry
                    {
                        ShiftTaskId = stable.Id, ImmId = immId,
                        ActualPartWeightGrams = 4.5m, ManualFullCycleSeconds = 55,
                        ActualPackagingUnit = 25, PackagingPlaces = 1.25m,
                        ManualGoodQuantity = 8, ManualDefectQuantity = 2
                    },
                    new ShiftClosureEntry
                    {
                        ShiftTaskId = cancelled.Id, ImmId = immId, ManualGoodQuantity = 99
                    }
                }
            });
            await db.SaveChangesAsync();
        }
        var client = await ClientAsync(lead);
        var before = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));
        Entry(before, stable.Id).GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(4.5m);
        Entry(before, stable.Id).GetProperty("manualFullCycleSeconds").GetInt32().Should().Be(55);
        Entry(before, stable.Id).GetProperty("actualPackagingUnit").GetInt32().Should().Be(25);
        Entry(before, stable.Id).GetProperty("packagingPlaces").GetDecimal().Should().Be(1.25m);
        Entry(before, stable.Id).GetProperty("manualGoodQuantity").GetInt32().Should().Be(8);
        Entry(before, stable.Id).GetProperty("manualDefectQuantity").GetInt32().Should().Be(2);
        Entry(before, cancelled.Id).GetProperty("manualGoodQuantity").GetInt32().Should().Be(99);

        var added = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(30));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = await db.ShiftTasks.SingleAsync(t => t.Id == cancelled.Id);
            task.Status = TaskStatus.Closed;
            task.ClosedAt = Utc(now);
            await db.SaveChangesAsync();
        }

        var after = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));
        var ids = after.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("taskId").GetGuid()).ToList();
        ids.Should().Contain(new[] { stable.Id, added.Id, startedClosed.Id });
        ids.Should().NotContain(cancelled.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftClosureEntries.AnyAsync(e => e.ShiftTaskId == cancelled.Id)).Should().BeTrue(
                "GET не удаляет сохранённый ручной ввод исчезнувшего задания");
        }
    }

    [Fact]
    public async Task Draft_DistributesMergedIntervalsTaskOutputsZeroSharesAndOnlyPositiveOrphans()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-5));
        var end = Utc(now.AddHours(-1));
        var split1 = start.AddMinutes(30);
        var split2 = start.AddHours(1);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task1 = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        var task2 = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        await SeedAssignmentAsync(item.Id, immId, first, slot: 1, split1, split2);
        await SeedAssignmentAsync(item.Id, immId, second, slot: 2, split1, split2);
        await SeedCycleAsync(task1, split1.AddMinutes(10), cavities: 3);
        await SeedCycleAsync(task1, split2.AddMinutes(10), cavities: 2);
        await SeedCycleAsync(task2, split2.AddMinutes(20), cavities: 2);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));
        var timeline = body.GetProperty("timelines").EnumerateArray()
            .Single(t => t.GetProperty("immId").GetGuid() == immId);
        timeline.GetProperty("immName").GetString().Should().NotBeNullOrWhiteSpace();
        var intervals = timeline.GetProperty("intervals").EnumerateArray().ToList();
        intervals.Should().HaveCount(3);
        intervals[0].GetProperty("startAtUtc").GetDateTime().Should().BeCloseTo(start, TimeSpan.FromSeconds(1));
        intervals[0].GetProperty("endAtUtc").GetDateTime().Should().BeCloseTo(split1, TimeSpan.FromSeconds(1));
        intervals[0].GetProperty("operators").EnumerateArray().Should().BeEmpty();
        intervals[0].GetProperty("automaticOutputQuantity").GetInt32().Should().Be(0);
        intervals[0].GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse(
            "нулевой выпуск без литейщиков не является сиротой");

        var staffed = intervals[1];
        staffed.GetProperty("startAtUtc").GetDateTime().Should().BeCloseTo(split1, TimeSpan.FromSeconds(1));
        staffed.GetProperty("endAtUtc").GetDateTime().Should().BeCloseTo(split2, TimeSpan.FromSeconds(1));
        staffed.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(3);
        staffed.GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse();
        staffed.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("operatorId").GetString())
            .Should().BeEquivalentTo(new[] { first.Id, second.Id });
        var staffedOutputs = staffed.GetProperty("taskOutputs").EnumerateArray().ToList();
        staffedOutputs.Single(o => o.GetProperty("taskId").GetGuid() == task1.Id)
            .GetProperty("quantity").GetInt32().Should().Be(3);
        staffedOutputs.Single(o => o.GetProperty("taskId").GetGuid() == task2.Id)
            .GetProperty("quantity").GetInt32().Should().Be(0);

        intervals[2].GetProperty("operators").EnumerateArray().Should().BeEmpty();
        intervals[2].GetProperty("automaticOutputQuantity").GetInt32().Should().Be(4);
        intervals[2].GetProperty("hasOrphanOutput").GetBoolean().Should().BeTrue();
        intervals[2].GetProperty("taskOutputs").EnumerateArray().Should().HaveCount(2);

        var shares1 = Entry(body, task1.Id).GetProperty("outputShares").EnumerateArray().ToList();
        shares1.Single(s => s.GetProperty("operatorId").GetString() == first.Id)
            .GetProperty("quantity").GetInt32().Should().Be(2);
        shares1.Single(s => s.GetProperty("operatorId").GetString() == second.Id)
            .GetProperty("quantity").GetInt32().Should().Be(1);
        var totals1 = Entry(body, task1.Id).GetProperty("operatorTotals").EnumerateArray().ToList();
        totals1.Single(s => s.GetProperty("operatorId").GetString() == first.Id)
            .GetProperty("quantity").GetInt32().Should().Be(2);
        totals1.Single(s => s.GetProperty("operatorId").GetString() == second.Id)
            .GetProperty("quantity").GetInt32().Should().Be(1);
        Entry(body, task2.Id).GetProperty("outputShares").EnumerateArray()
            .Should().OnlyContain(s => s.GetProperty("quantity").GetInt32() == 0);
        Entry(body, task2.Id).GetProperty("operatorTotals").EnumerateArray()
            .Should().OnlyContain(s => s.GetProperty("quantity").GetInt32() == 0);
        Entry(body, task1.Id).GetProperty("hasOperators").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Draft_MarksRawOfflineFallbackButNotOnlineIdle()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-3));
        var end = Utc(now.AddHours(-1));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var offlineImm = await SeedImmAsync();
        var idleImm = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var offlineTask = await SeedTaskAsync(offlineImm, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        var idleTask = await SeedTaskAsync(idleImm, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        await SeedAssignmentAsync(item.Id, offlineImm, operatorUser, 1, start, null);
        await SeedAssignmentAsync(item.Id, idleImm, operatorUser, 1, start, null);
        await SeedStatusAsync(offlineImm, ImmStatus.Offline, start, end);
        await SeedStatusAsync(idleImm, ImmStatus.Idle, start, end);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        Entry(body, offlineTask.Id).GetProperty("isManualFallbackRequired").GetBoolean().Should().BeTrue();
        Entry(body, idleTask.Id).GetProperty("isManualFallbackRequired").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Pending_ReturnsOnlyEndedUnclosedAccessibleShiftsAndDoesNotDependOnNearest()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var older = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-8), now.AddHours(-7)))).Items.Single();
        var newer = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-5), now.AddHours(-4)))).Items.Single();
        var foreign = (await SeedDayAsync(Item(1, other.Id, now.AddHours(-3), now.AddHours(-2)))).Items.Single();
        var future = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(1), now.AddHours(3)))).Items.Single();
        var closed = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-12), now.AddHours(-11)))).Items.Single();
        await SeedEmptyClosureAsync(closed, lead.Id, ShiftClosureState.Closed);
        var immId = await SeedImmAsync();
        var orphanTask = await SeedTaskAsync(immId, TaskStatus.Completed, now.AddDays(1),
            setupStartedAt: newer.StartAtUtc, startedAt: newer.StartAtUtc,
            completedAt: newer.EndAtUtc);
        await SeedCycleAsync(orphanTask, newer.StartAtUtc.AddMinutes(30), cavities: 2);
        var leadClient = await ClientAsync(lead);

        var leadBody = await RequiredJsonAsync(await leadClient.GetAsync("/api/shift-closures/pending"));
        var leadItems = leadBody.EnumerateArray().ToList();
        leadItems.Select(i => i.GetProperty("shiftPlanItemId").GetGuid()).Should().Equal(newer.Id, older.Id);
        leadItems.Select(i => i.GetProperty("shiftPlanItemId").GetGuid()).Should()
            .NotContain(new[] { foreign.Id, future.Id, closed.Id });
        leadItems[0].GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
        leadItems[0].GetProperty("shiftLeadName").GetString().Should().Be(lead.FullName);
        leadItems[0].GetProperty("hasOrphanOutput").GetBoolean().Should().BeTrue();
        leadItems[1].GetProperty("hasOrphanOutput").GetBoolean().Should().BeFalse();

        foreach (var client in new[] { await ManagerClientAsync(), await AdminClientAsync() })
        {
            var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-closures/pending"));
            body.EnumerateArray().Select(i => i.GetProperty("shiftPlanItemId").GetGuid()).Should()
                .Contain(new[] { newer.Id, older.Id, foreign.Id });
        }
    }

    [Fact]
    public async Task Pending_UsesEffectiveDefaultAndManagerFallbackForMaterializedEndedNonclosed()
    {
        var defaultPosition = Interlocked.Increment(ref _positionOffset);
        var fallbackPosition = Interlocked.Increment(ref _positionOffset);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var previousDefault = await SetDefaultAsync(defaultPosition, lead.Id);
        try
        {
            var now = DateTime.UtcNow;
            var defaultItem = (await SeedDayAsync(Item(defaultPosition, null,
                now.AddHours(-8), now.AddHours(-7)))).Items.Single();
            var fallbackItem = (await SeedDayAsync(Item(fallbackPosition, null,
                now.AddHours(-6), now.AddHours(-5)))).Items.Single();
            var future = (await SeedDayAsync(Item(fallbackPosition, null,
                now.AddHours(1), now.AddHours(2)))).Items.Single();
            var closed = (await SeedDayAsync(Item(fallbackPosition, null,
                now.AddHours(-4), now.AddHours(-3)))).Items.Single();
            var fallbackManager = await FirstActiveManagerAsync();
            await SeedEmptyClosureAsync(closed, fallbackManager.Id, ShiftClosureState.Closed);
            var leadClient = await ClientAsync(lead);

            var leadBody = await RequiredJsonAsync(await leadClient.GetAsync(
                "/api/shift-closures/pending"));
            leadBody.EnumerateArray().Should().ContainSingle(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == defaultItem.Id);
            leadBody.EnumerateArray().Should().NotContain(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == fallbackItem.Id);

            var managerBody = await RequiredJsonAsync(await (await ManagerClientAsync()).GetAsync(
                "/api/shift-closures/pending"));
            var defaultDto = managerBody.EnumerateArray().Should().ContainSingle(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == defaultItem.Id).Subject;
            defaultDto.GetProperty("shiftLeadId").GetString().Should().Be(lead.Id);
            defaultDto.GetProperty("shiftLeadName").GetString().Should().Be(lead.FullName);
            var fallbackDto = managerBody.EnumerateArray().Should().ContainSingle(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == fallbackItem.Id).Subject;
            fallbackDto.GetProperty("shiftLeadId").GetString().Should().Be(fallbackManager.Id);
            fallbackDto.GetProperty("shiftLeadName").GetString().Should().Be(fallbackManager.FullName);
            managerBody.EnumerateArray().Select(i => i.GetProperty("shiftPlanItemId").GetGuid())
                .Should().NotContain(new[] { future.Id, closed.Id });
        }
        finally
        {
            await SetDefaultAsync(defaultPosition, previousDefault);
        }
    }

    [Fact]
    public async Task Read_EnforcesOwnershipRolesMissingItemAndRequiredShiftLead()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var own = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var foreign = (await SeedDayAsync(Item(1, other.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var withoutLead = (await SeedDayAsync(Item(Interlocked.Increment(ref _positionOffset), null,
            now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var leadClient = await ClientAsync(lead);
        (await leadClient.GetAsync($"/api/shift-closures/items/{own.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await leadClient.GetAsync($"/api/shift-closures/items/{foreign.Id}")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await leadClient.GetAsync($"/api/shift-closures/items/{Guid.NewGuid()}")).StatusCode.Should()
            .Be(HttpStatusCode.NotFound);

        foreach (var role in new[] { UserRole.Operator, UserRole.Adjuster, UserRole.Observer })
        {
            var user = await SeedUserAsync(role);
            var client = await ClientAsync(user);
            (await client.GetAsync($"/api/shift-closures/items/{own.Id}")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden, role.ToString());
            (await client.GetAsync("/api/shift-closures/pending")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden, role.ToString());
        }

        var adminClient = await AdminClientAsync();
        var deactivatedManagers = await DeactivateActiveManagersAsync();
        try
        {
            var response = await adminClient.GetAsync(
                $"/api/shift-closures/items/{withoutLead.Id}");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            error.GetProperty("message").GetString().Should().Be("shiftLeadRequired");
        }
        finally
        {
            await RestoreManagersAsync(deactivatedManagers);
        }
    }

    [Fact]
    public async Task BeforeStart_ReturnsPlanAndEntriesWithEmptyAutomaticWindowAndNoNegativeIntervals()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(2));
        var end = Utc(now.AddHours(4));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Planned, start.AddMinutes(15),
            planQuantity: 10, cavities: 1, fullCycleSeconds: 60);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        body.GetProperty("factEndUtc").GetDateTime().Should().BeBefore(start);
        Entry(body, task.Id).GetProperty("shiftPlanQuantity").GetInt32().Should().Be(10);
        Entry(body, task.Id).GetProperty("automaticOutputQuantity").GetInt32().Should().Be(0);
        Entry(body, task.Id).GetProperty("outputShares").EnumerateArray().Should().BeEmpty();
        var intervals = body.GetProperty("timelines").EnumerateArray()
            .SelectMany(t => t.GetProperty("intervals").EnumerateArray()).ToList();
        intervals.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosedRead_ProjectsFrozenEntrySnapshotsDespiteCurrentTaskCyclesAndStatuses()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var (_, productId) = await _factory.CreateProductTypeAsync();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            planQuantity: 10, cavities: 1, partWeight: 5, fullCycleSeconds: 60,
            productId: productId,
            setupStartedAt: start, startedAt: start, completedAt: end);
        await SeedAssignmentAsync(item.Id, immId, operatorUser, 1, start, null);
        Guid closureId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var closure = new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = ShiftClosureState.Closed,
                CreatedById = lead.Id,
                ClosedById = lead.Id,
                ClosedAt = end,
                Entries =
                {
                    new ShiftClosureEntry
                    {
                        ShiftTaskId = task.Id,
                        ImmId = immId,
                        ImmNameSnapshot = "SNAPSHOT IMM",
                        ProductNameSnapshot = "SNAPSHOT PRODUCT",
                        PlasticIngredientsSnapshot = "SNAPSHOT PLASTIC",
                        ColorantIngredientsSnapshot = "SNAPSHOT COLORANT",
                        RemainingAtStart = 7,
                        NormPartWeightGrams = 4,
                        PlannedFullCycleSeconds = 50,
                        TaskPackagingUnit = 10,
                        Cavities = 1,
                        ShiftPlanQuantity = 6,
                        PlannedMaterialWeightGrams = 24,
                        AutomaticOutputQuantity = 3,
                        AutomaticAverageFullCycleSeconds = 42,
                        IsManualFallbackRequired = true,
                        ActualPartWeightGrams = 4.2m,
                        ManualGoodQuantity = 2,
                        ManualDefectQuantity = 1,
                        OutputShares =
                        {
                            new ShiftClosureOutputShare
                            {
                                IntervalStartUtc = start,
                                IntervalEndUtc = end,
                                OperatorId = operatorUser.Id,
                                OperatorFullNameSnapshot = "SNAPSHOT OPERATOR",
                                OperatorEmployeeIdSnapshot = "SNAP-1",
                                SlotAtIntervalStart = 1,
                                Quantity = 3
                            }
                        }
                    }
                }
            };
            db.ShiftClosures.Add(closure);
            await db.SaveChangesAsync();
            closureId = closure.Id;
        }
        await SeedCycleAsync(task, start.AddMinutes(30), cavities: 100, durationSeconds: 100);
        await SeedStatusAsync(immId, ImmStatus.Idle, start, end);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var current = await db.ShiftTasks.Include(t => t.Imm).Include(t => t.Product)
                .SingleAsync(t => t.Id == task.Id);
            current.Imm.Name = "CURRENT IMM";
            current.Product!.Name = "CURRENT PRODUCT";
            current.PartWeightGrams = 99;
            await db.SaveChangesAsync();
        }
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        body.GetProperty("id").GetGuid().Should().Be(closureId);
        body.GetProperty("state").GetString().Should().Be("Closed");
        body.GetProperty("closedAt").GetDateTime().Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
        body.GetProperty("factEndUtc").GetDateTime().Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
        body.GetProperty("closedEarly").GetBoolean().Should().BeFalse();
        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.GetProperty("immName").GetString().Should().Be("SNAPSHOT IMM");
        entry.GetProperty("productName").GetString().Should().Be("SNAPSHOT PRODUCT");
        entry.GetProperty("plasticIngredients").GetString().Should().Be("SNAPSHOT PLASTIC");
        entry.GetProperty("colorantIngredients").GetString().Should().Be("SNAPSHOT COLORANT");
        entry.GetProperty("remainingAtStart").GetInt32().Should().Be(7);
        entry.GetProperty("normPartWeightGrams").GetDecimal().Should().Be(4);
        entry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(6);
        entry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(3);
        entry.GetProperty("automaticAverageFullCycleSeconds").GetDouble().Should().Be(42);
        entry.GetProperty("isManualFallbackRequired").GetBoolean().Should().BeTrue();
        var share = entry.GetProperty("outputShares").EnumerateArray().Single();
        share.GetProperty("operatorFullName").GetString().Should().Be("SNAPSHOT OPERATOR");
        share.GetProperty("operatorEmployeeId").GetString().Should().Be("SNAP-1");
        share.GetProperty("quantity").GetInt32().Should().Be(3);
        entry.GetProperty("operatorTotals").EnumerateArray().Single()
            .GetProperty("quantity").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Draft_MarksOrphanOutputWhenItOnlyPartiallyOverlapsMergedInterval()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var activityStart = start.AddHours(1);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: activityStart, startedAt: activityStart, completedAt: end);
        await SeedCycleAsync(task, activityStart.AddMinutes(15), cavities: 2);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        var interval = body.GetProperty("timelines").EnumerateArray()
            .Single(t => t.GetProperty("immId").GetGuid() == immId)
            .GetProperty("intervals").EnumerateArray().Single();
        interval.GetProperty("startAtUtc").GetDateTime().Should().BeCloseTo(start, TimeSpan.FromSeconds(1));
        interval.GetProperty("operators").EnumerateArray().Should().BeEmpty();
        interval.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(2);
        interval.GetProperty("hasOrphanOutput").GetBoolean().Should().BeTrue(
            "сирота обрезан по началу активности задания, но пересекает объединённый интервал");
    }

    [Fact]
    public async Task Pending_MarksOrphanOutputWhenItOnlyPartiallyOverlapsMergedInterval()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var activityStart = start.AddHours(1);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: activityStart, startedAt: activityStart, completedAt: end);
        await SeedCycleAsync(task, activityStart.AddMinutes(15), cavities: 2);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-closures/pending"));

        body.EnumerateArray().Single(i => i.GetProperty("shiftPlanItemId").GetGuid() == item.Id)
            .GetProperty("hasOrphanOutput").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Draft_TimelineTaskOutputsConserveCyclesOutsideTaskActivity()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var activityStart = start.AddHours(1);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: activityStart, startedAt: activityStart, completedAt: end);
        await SeedAssignmentAsync(item.Id, immId, operatorUser, 1, start, null);
        await SeedCycleAsync(task, start.AddMinutes(15), cavities: 5);
        await SeedCycleAsync(task, activityStart.AddMinutes(15), cavities: 3);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        var interval = body.GetProperty("timelines").EnumerateArray()
            .Single(t => t.GetProperty("immId").GetGuid() == immId)
            .GetProperty("intervals").EnumerateArray().Single();
        interval.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(8,
            "ретро-цикл до активности остаётся в выпуске и распределении строки");
        interval.GetProperty("taskOutputs").EnumerateArray().Single()
            .GetProperty("quantity").GetInt32().Should().Be(8);
        Entry(body, task.Id).GetProperty("outputShares").EnumerateArray()
            .Sum(s => s.GetProperty("quantity").GetInt32()).Should().Be(8);
    }

    [Fact]
    public async Task ClosedRead_RebuildsZeroOutputUnstaffedIntervalsFromAssignmentJournal()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var staffedStart = start.AddMinutes(30);
        var staffedEnd = start.AddMinutes(90);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        await SeedAssignmentAsync(item.Id, immId, operatorUser, 1, staffedStart, staffedEnd);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = ShiftClosureState.Closed,
                CreatedById = lead.Id,
                ClosedById = lead.Id,
                ClosedAt = end,
                Entries =
                {
                    new ShiftClosureEntry
                    {
                        ShiftTaskId = task.Id,
                        ImmId = immId,
                        ImmNameSnapshot = "SNAPSHOT IMM",
                        AutomaticOutputQuantity = 3,
                        OutputShares =
                        {
                            new ShiftClosureOutputShare
                            {
                                IntervalStartUtc = staffedStart,
                                IntervalEndUtc = staffedEnd,
                                OperatorId = operatorUser.Id,
                                OperatorFullNameSnapshot = "SNAPSHOT OPERATOR",
                                SlotAtIntervalStart = 1,
                                Quantity = 3
                            }
                        }
                    }
                }
            });
            await db.SaveChangesAsync();
        }
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        var intervals = body.GetProperty("timelines").EnumerateArray().Single()
            .GetProperty("intervals").EnumerateArray().ToList();
        intervals.Should().HaveCount(3);
        var expectedBounds = new[]
        {
            (Start: start, End: staffedStart),
            (Start: staffedStart, End: staffedEnd),
            (Start: staffedEnd, End: end)
        };
        for (var i = 0; i < intervals.Count; i++)
        {
            intervals[i].GetProperty("startAtUtc").GetDateTime().Should()
                .BeCloseTo(expectedBounds[i].Start, TimeSpan.FromSeconds(1));
            intervals[i].GetProperty("endAtUtc").GetDateTime().Should()
                .BeCloseTo(expectedBounds[i].End, TimeSpan.FromSeconds(1));
        }
        intervals[0].GetProperty("operators").EnumerateArray().Should().BeEmpty();
        intervals[0].GetProperty("automaticOutputQuantity").GetInt32().Should().Be(0);
        intervals[0].GetProperty("taskOutputs").EnumerateArray().Single()
            .GetProperty("quantity").GetInt32().Should().Be(0);
        intervals[1].GetProperty("operators").EnumerateArray().Single()
            .GetProperty("operatorId").GetString().Should().Be(operatorUser.Id);
        intervals[1].GetProperty("automaticOutputQuantity").GetInt32().Should().Be(3);
        intervals[1].GetProperty("taskOutputs").EnumerateArray().Single()
            .GetProperty("quantity").GetInt32().Should().Be(3);
        intervals[2].GetProperty("operators").EnumerateArray().Should().BeEmpty();
        intervals[2].GetProperty("automaticOutputQuantity").GetInt32().Should().Be(0);
        intervals[2].GetProperty("taskOutputs").EnumerateArray().Single()
            .GetProperty("quantity").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ClosedRead_TimelineResolvesCurrentUserForJournalOperatorWithoutShares()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(1, lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, TaskStatus.Completed, end.AddDays(1),
            setupStartedAt: start, startedAt: start, completedAt: end);
        await SeedAssignmentAsync(item.Id, immId, operatorUser, 1, start, null);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = item.Id,
                State = ShiftClosureState.Closed,
                CreatedById = lead.Id,
                ClosedById = lead.Id,
                ClosedAt = end,
                Entries =
                {
                    new ShiftClosureEntry
                    {
                        ShiftTaskId = task.Id,
                        ImmId = immId,
                        ImmNameSnapshot = "SNAPSHOT IMM",
                        AutomaticOutputQuantity = 0
                    }
                }
            });
            await db.SaveChangesAsync();
            var current = await db.Users.SingleAsync(u => u.Id == operatorUser.Id);
            current.FullName = "ПЕРЕИМЕНОВАННЫЙ ЛИТЕЙЩИК";
            current.EmployeeId = "CURRENT-77";
            await db.SaveChangesAsync();
        }
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        var timelineOperator = body.GetProperty("timelines").EnumerateArray().Single()
            .GetProperty("intervals").EnumerateArray().Single()
            .GetProperty("operators").EnumerateArray().Single();
        timelineOperator.GetProperty("operatorId").GetString().Should().Be(operatorUser.Id);
        timelineOperator.GetProperty("fullName").GetString().Should().Be("ПЕРЕИМЕНОВАННЫЙ ЛИТЕЙЩИК");
        timelineOperator.GetProperty("employeeId").GetString().Should().Be("CURRENT-77");
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

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"closure_{role}_{suffix}";
        const string password = "Passw0rd!";
        var fullName = $"{role} {suffix}";
        var employeeId = $"EMP-{suffix}"[..12];
        var user = new User
        {
            UserName = login,
            NormalizedUserName = login.ToUpperInvariant(),
            Email = $"{login}@test.local",
            NormalizedEmail = $"{login.ToUpperInvariant()}@TEST.LOCAL",
            FullName = fullName,
            EmployeeId = employeeId,
            Role = role,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password, fullName, employeeId);
    }

    private async Task<Guid> SeedImmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"CLOSURE-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(int position, string? leadId, DateTime start, DateTime end) => new()
    {
        Position = position,
        ShiftLeadId = leadId,
        StartAtUtc = Utc(start),
        EndAtUtc = Utc(end),
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
        int planQuantity = 10,
        int cavities = 1,
        decimal partWeight = 5,
        int fullCycleSeconds = 60,
        int? packagingUnit = null,
        Guid? productId = null,
        DateTime? setupStartedAt = null,
        DateTime? startedAt = null,
        DateTime? completedAt = null,
        DateTime? closedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var local = schedule.ToLocal(Utc(plannedStartAt));
        var task = new ShiftTask
        {
            ImmId = immId,
            MoldId = _factory.TestMoldId,
            ProductId = productId ?? _factory.TestProductId,
            PlanQuantity = planQuantity,
            Cavities = cavities,
            PartWeightGrams = partWeight,
            PackagingUnit = packagingUnit,
            Status = status,
            PlannedDate = DateOnly.FromDateTime(local),
            PlannedSetupStartTime = TimeOnly.FromDateTime(local),
            PlannedSetupMinutes = 15,
            PlannedFullCycleSeconds = fullCycleSeconds,
            SetupStartedAt = Utc(setupStartedAt),
            StartedAt = Utc(startedAt),
            CompletedAt = Utc(completedAt),
            ClosedAt = Utc(closedAt),
            IssuedAt = status == TaskStatus.Issued ? Utc(DateTime.UtcNow) : null
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private async Task SeedCycleAsync(
        ShiftTask task, DateTime endTime, int cavities, int durationSeconds = 60,
        bool isSuccessful = true, bool open = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var end = Utc(endTime);
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = task.ImmId,
            TaskId = task.Id,
            MoldId = task.MoldId,
            StartTime = end.AddSeconds(-durationSeconds),
            EndTime = open ? null : end,
            DurationSeconds = durationSeconds,
            Cavities = cavities,
            IsSuccessful = isSuccessful
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAssignmentAsync(
        Guid itemId, Guid immId, TestUser operatorUser, int slot,
        DateTime assignedAt, DateTime? unassignedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = itemId,
            ImmId = immId,
            OperatorId = operatorUser.Id,
            Slot = slot,
            AssignedAt = Utc(assignedAt),
            UnassignedAt = Utc(unassignedAt),
            CreatedById = operatorUser.Id
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedStatusAsync(Guid immId, string status, DateTime start, DateTime end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ImmStatusHistory.Add(new ImmStatusHistory
        {
            ImmId = immId,
            Status = status,
            ChangedAt = Utc(start),
            EndedAt = Utc(end)
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedEmptyClosureAsync(
        ShiftPlanItem item, string leadId, ShiftClosureState state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftClosures.Add(new ShiftClosure
        {
            ShiftPlanItemId = item.Id,
            State = state,
            CreatedById = leadId,
            ClosedById = state == ShiftClosureState.Closed ? leadId : null,
            ClosedAt = state == ShiftClosureState.Closed ? Utc(item.EndAtUtc) : null
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> DeactivateActiveManagersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var ids = await db.Users.Where(u => u.IsActive && u.Role == UserRole.Manager)
            .Select(u => u.Id).ToListAsync();
        await db.Users.Where(u => ids.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        return ids;
    }

    private async Task RestoreManagersAsync(IReadOnlyCollection<string> ids)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Users.Where(u => ids.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, true));
    }

    private async Task<string?> SetDefaultAsync(int position, string? leadId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var existing = await db.ShiftDefaultLeads.SingleOrDefaultAsync(d => d.Position == position);
        var previous = existing?.ShiftLeadId;
        if (existing != null) db.ShiftDefaultLeads.Remove(existing);
        if (leadId != null)
            db.ShiftDefaultLeads.Add(new ShiftDefaultLead { Position = position, ShiftLeadId = leadId });
        await db.SaveChangesAsync();
        return previous;
    }

    private async Task<LeadIdentity> FirstActiveManagerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var manager = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .FirstAsync();
        return new LeadIdentity(manager.Id, manager.FullName);
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static JsonElement Entry(JsonElement body, Guid taskId) => body.GetProperty("entries")
        .EnumerateArray().Should().ContainSingle(e => e.GetProperty("taskId").GetGuid() == taskId).Subject;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
}
