using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.ShiftClosures;

[Collection("Integration")]
public class ShiftClosureCloseApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 9000;
    private static int _windowOffset;
    private static int _positionOffset = 31000;
    private readonly IntegrationTestFactory _factory;

    public ShiftClosureCloseApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(
        string Id, string Login, string Password, string FullName, string? EmployeeId);

    private sealed record FieldError(Guid TaskId, string Field, string Code);

    private sealed record TaskFacts(
        DateTime? UpdatedAt,
        int ActualQuantity,
        int DefectQuantity,
        decimal ActualMaterialWeightGrams,
        TaskStatus Status);

    private sealed record LeadIdentity(string Id, string FullName, DateTime CreatedAt);

    [Fact]
    public async Task Close_CreatesCompleteSnapshotForAllCurrentRowsAndZeroSharesAndRepeatReturns409()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var (_, productId) = await _factory.CreateProductTypeAsync();
        await SetProductSnapshotSourceAsync(productId, "SNAPSHOT PRODUCT", runnerWeight: 2);
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var supplied = await SeedTaskAsync(immId, start, end, planQuantity: 12, cavities: 2,
            partWeight: 4, packagingUnit: 20, productId: productId);
        var untouched = await SeedTaskAsync(immId, start, end, planQuantity: 6,
            cavities: 1, partWeight: 6, packagingUnit: 10);
        var disappeared = await SeedTaskAsync(immId, start, end, status: TaskStatus.Planned,
            plannedStartAt: start.AddMinutes(20));
        await SeedRecipeAsync(supplied.Id);
        await SeedAssignmentAsync(item.Id, immId, first.Id, 1, start, null);
        await SeedAssignmentAsync(item.Id, immId, second.Id, 2, start, null);
        await SeedCycleAsync(supplied, start.AddMinutes(30), cavities: 3, durationSeconds: 40);
        await SeedCycleAsync(supplied, start.AddMinutes(50), cavities: 1, durationSeconds: 60);
        await SeedStatusAsync(immId, ImmStatus.Offline, start, end);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(supplied.Id, 4.2m, 55, 24, 1.25m, 8, 2),
            Manual(untouched.Id, manualFullCycleSeconds: 65, manualGoodQuantity: 0, manualDefectQuantity: 0),
            Manual(disappeared.Id, manualGoodQuantity: 99)));
        await MakeUnstartedTaskDisappearAsync(disappeared.Id);

        var response = await CloseAsync(client, item.Id);
        var body = await RequiredJsonAsync(response);

        body.GetProperty("state").GetString().Should().Be("Closed");
        body.GetProperty("closedById").GetString().Should().Be(lead.Id);
        body.GetProperty("closedByName").GetString().Should().Be(lead.FullName);
        body.GetProperty("closedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        body.GetProperty("closedAt").GetDateTime().Kind.Should().Be(DateTimeKind.Utc);
        body.GetProperty("closedEarly").GetBoolean().Should().BeFalse();
        body.GetProperty("factEndUtc").GetDateTime().Should().Be(end);
        body.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("taskId").GetGuid())
            .Should().BeEquivalentTo(new[] { supplied.Id, untouched.Id });

        var entry = Entry(body, supplied.Id);
        entry.GetProperty("immName").GetString().Should().StartWith("CLOSE-");
        entry.GetProperty("productName").GetString().Should().Be("SNAPSHOT PRODUCT");
        entry.GetProperty("plasticIngredients").GetString().Should().StartWith("CLOSE PLASTIC ");
        entry.GetProperty("colorantIngredients").GetString().Should().StartWith("CLOSE COLORANT ");
        entry.GetProperty("remainingAtStart").GetInt32().Should().Be(12);
        entry.GetProperty("normPartWeightGrams").GetDecimal().Should().Be(4);
        entry.GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(4.2m);
        entry.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(60);
        entry.GetProperty("automaticAverageFullCycleSeconds").GetDouble().Should().Be(50);
        entry.GetProperty("manualFullCycleSeconds").GetInt32().Should().Be(55);
        entry.GetProperty("taskPackagingUnit").GetInt32().Should().Be(20);
        entry.GetProperty("actualPackagingUnit").GetInt32().Should().Be(24);
        entry.GetProperty("packagingPlaces").GetDecimal().Should().Be(1.25m);
        entry.GetProperty("cavities").GetInt32().Should().Be(2);
        entry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(12);
        entry.GetProperty("plannedMaterialWeightGrams").GetDecimal().Should().Be(60);
        entry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(4);
        entry.GetProperty("manualGoodQuantity").GetInt32().Should().Be(8);
        entry.GetProperty("manualDefectQuantity").GetInt32().Should().Be(2);
        entry.GetProperty("isManualFallbackRequired").GetBoolean().Should().BeTrue();
        var suppliedShares = Shares(entry);
        suppliedShares.Should().HaveCount(2);
        suppliedShares.Select(s => s.GetProperty("operatorId").GetString()).Should()
            .BeEquivalentTo(new[] { first.Id, second.Id });
        suppliedShares.Should().OnlyContain(s => s.GetProperty("quantity").GetInt32() == 2);
        var untouchedEntry = Entry(body, untouched.Id);
        untouchedEntry.GetProperty("actualPartWeightGrams").ValueKind.Should().Be(JsonValueKind.Null);
        var zeroShares = Shares(untouchedEntry);
        zeroShares.Should().HaveCount(2);
        zeroShares.Select(s => s.GetProperty("operatorId").GetString()).Should()
            .BeEquivalentTo(new[] { first.Id, second.Id });
        zeroShares.Should().OnlyContain(s => s.GetProperty("quantity").GetInt32() == 0);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var persisted = await db.ShiftClosureEntries.AsNoTracking()
                .Where(e => e.ShiftClosure.ShiftPlanItemId == item.Id).ToListAsync();
            persisted.Select(e => e.ShiftTaskId).Should().BeEquivalentTo(new[] { supplied.Id, untouched.Id });
            persisted.Should().NotContain(e => e.ShiftTaskId == disappeared.Id);
            var suppliedSnapshot = persisted.Single(e => e.ShiftTaskId == supplied.Id);
            suppliedSnapshot.ImmNameSnapshot.Should().StartWith("CLOSE-");
            suppliedSnapshot.ProductNameSnapshot.Should().Be("SNAPSHOT PRODUCT");
            suppliedSnapshot.PlasticIngredientsSnapshot.Should().StartWith("CLOSE PLASTIC ");
            suppliedSnapshot.ColorantIngredientsSnapshot.Should().StartWith("CLOSE COLORANT ");
            suppliedSnapshot.AutomaticOutputQuantity.Should().Be(4);
            suppliedSnapshot.AutomaticAverageFullCycleSeconds.Should().Be(50);
            suppliedSnapshot.ManualGoodQuantity.Should().Be(8);
            (await db.ShiftClosureOutputShares.CountAsync(s => s.ShiftClosureEntry.ShiftClosureId == body.GetProperty("id").GetGuid()))
                .Should().Be(4);
        }

        (await CloseAsync(client, item.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Close_EarlyRequiresConfirmationClipsOnlyFactAndUsesServerAudit()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-1));
        var end = Utc(now.AddHours(3));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, planQuantity: 10, cavities: 1,
            partWeight: 5, packagingUnit: 20);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        await SeedCycleAsync(task, now.AddMinutes(-10), cavities: 2, durationSeconds: 40);
        await SeedCycleAsync(task, now.AddMinutes(30), cavities: 4, durationSeconds: 60);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, actualPartWeightGrams: 5, actualPackagingUnit: 20)));

        var rejected = await CloseAsync(client, item.Id);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(rejected)).GetProperty("code").GetString().Should()
            .Be("earlyCloseConfirmationRequired");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftClosures.SingleAsync(c => c.ShiftPlanItemId == item.Id)).State.Should()
                .Be(ShiftClosureState.Draft);
        }

        var body = await RequiredJsonAsync(await CloseAsync(client, item.Id, confirmEarly: true));
        var closedAt = body.GetProperty("closedAt").GetDateTime();
        closedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        closedAt.Kind.Should().Be(DateTimeKind.Utc);
        body.GetProperty("closedById").GetString().Should().Be(lead.Id);
        body.GetProperty("closedEarly").GetBoolean().Should().BeTrue();
        body.GetProperty("factEndUtc").GetDateTime().Should().Be(closedAt);
        var entry = Entry(body, task.Id);
        entry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(2,
            "цикл после фактического конца досрочного закрытия не входит в снимок");
        entry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(10,
            "досрочное закрытие не урезает план полной смены");
    }

    [Fact]
    public async Task Close_PositiveOrphanBlocksRetroAssignmentRemovesBlockAndZeroOutputDoesNotBlock()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 60);
        var zeroTask = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, actualPartWeightGrams: 5, actualPackagingUnit: 20)));

        var blocked = await CloseAsync(client, item.Id);
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(blocked)).GetProperty("message").GetString().Should().Be("orphanOutput");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftClosures.SingleAsync(c => c.ShiftPlanItemId == item.Id)).State.Should()
                .Be(ShiftClosureState.Draft);
        }

        var assigned = await client.PostAsJsonAsync($"/api/shift-staffing/items/{item.Id}/operators", new
        {
            immId,
            operatorId = operatorUser.Id,
            assignedAt = start,
            unassignedAt = end
        });
        assigned.StatusCode.Should().Be(HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        var body = await RequiredJsonAsync(await CloseAsync(client, item.Id));
        Shares(Entry(body, task.Id)).Single().GetProperty("quantity").GetInt32().Should().Be(2);
        Shares(Entry(body, zeroTask.Id)).Single().GetProperty("quantity").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData(false, "before")]
    [InlineData(false, "after")]
    [InlineData(true, "before")]
    [InlineData(true, "after")]
    public async Task Close_RetroCyclesOutsideTaskActivityAreConservedAsSharesOrOrphans(
        bool withOperator, string location)
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var tracked = await db.ShiftTasks.SingleAsync(t => t.Id == task.Id);
            tracked.SetupStartedAt = start.AddHours(1);
            tracked.StartedAt = start.AddHours(1);
            tracked.CompletedAt = end.AddHours(-1);
            await db.SaveChangesAsync();
        }
        if (withOperator)
            await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        var retroEnd = location == "before" ? start.AddMinutes(15) : end.AddMinutes(-15);
        await SeedCycleAsync(task, retroEnd, cavities: 7, durationSeconds: 60);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, actualPartWeightGrams: 5, actualPackagingUnit: 20)));

        var draft = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));
        var draftEntry = Entry(draft, task.Id);
        draftEntry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(7);
        var distributed = Shares(draftEntry).Sum(s => s.GetProperty("quantity").GetInt32());
        var timelineOutput = draft.GetProperty("timelines").EnumerateArray()
            .SelectMany(t => t.GetProperty("intervals").EnumerateArray())
            .SelectMany(i => i.GetProperty("taskOutputs").EnumerateArray())
            .Where(o => o.GetProperty("taskId").GetGuid() == task.Id)
            .Sum(o => o.GetProperty("quantity").GetInt32());
        (distributed + (withOperator ? 0 : timelineOutput)).Should().Be(7);
        timelineOutput.Should().Be(7);
        draft.GetProperty("timelines").EnumerateArray()
            .SelectMany(t => t.GetProperty("intervals").EnumerateArray())
            .Any(i => i.GetProperty("hasOrphanOutput").GetBoolean()).Should().Be(!withOperator);

        var close = await CloseAsync(client, item.Id);
        if (!withOperator)
        {
            close.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await JsonAsync(close)).GetProperty("message").GetString().Should().Be("orphanOutput");
            return;
        }

        var closed = await RequiredJsonAsync(close);
        var closedEntry = Entry(closed, task.Id);
        closedEntry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(7);
        Shares(closedEntry).Sum(s => s.GetProperty("quantity").GetInt32()).Should().Be(7);
    }

    [Fact]
    public async Task Close_FallbackAndOutputFieldsReturnTaskFieldErrorsUntilComplete()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, packagingUnit: null);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        await SeedStatusAsync(immId, ImmStatus.Offline, start, end);
        var client = await ClientAsync(lead);

        var fallbackErrors = await FieldErrorsAsync(await CloseAsync(client, item.Id));
        fallbackErrors.Should().BeEquivalentTo(new[]
        {
            new FieldError(task.Id, "manualFullCycleSeconds", "required"),
            new FieldError(task.Id, "manualGoodQuantity", "required"),
            new FieldError(task.Id, "manualDefectQuantity", "required")
        });

        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, manualFullCycleSeconds: 60, manualGoodQuantity: 2, manualDefectQuantity: 1)));
        var outputErrors = await FieldErrorsAsync(await CloseAsync(client, item.Id));
        outputErrors.Should().BeEquivalentTo(new[]
        {
            new FieldError(task.Id, "actualPartWeightGrams", "required"),
            new FieldError(task.Id, "actualPackagingUnit", "required")
        });

        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, 5, 60, 10, null, 2, 1)));
        var body = await RequiredJsonAsync(await CloseAsync(client, item.Id));
        Entry(body, task.Id).GetProperty("isManualFallbackRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Close_OrdinaryModeAllowsIncompleteManualFieldsAndManualAutomaticMismatch()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var mismatch = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        var incomplete = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        await SeedCycleAsync(mismatch, start.AddMinutes(30), cavities: 2, durationSeconds: 60);
        await SeedStatusAsync(immId, ImmStatus.Auto, start, end);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(mismatch.Id, 5, null, 20, null, 99, 1)));

        var body = await RequiredJsonAsync(await CloseAsync(client, item.Id));

        Entry(body, mismatch.Id).GetProperty("automaticOutputQuantity").GetInt32().Should().Be(2);
        Entry(body, mismatch.Id).GetProperty("manualGoodQuantity").GetInt32().Should().Be(99);
        Entry(body, mismatch.Id).GetProperty("isManualFallbackRequired").GetBoolean().Should().BeFalse();
        Entry(body, incomplete.Id).GetProperty("manualGoodQuantity").ValueKind.Should().Be(JsonValueKind.Null);
        Entry(body, incomplete.Id).GetProperty("manualDefectQuantity").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Close_UnstaffedRowForcesZeroManualOutputAndEmptyShiftClosesEmpty()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var unstaffedItem = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var emptyItem = (await SeedDayAsync(Item(lead.Id, start.AddDays(-1), end.AddDays(-1)))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end);
        await SeedStatusAsync(immId, ImmStatus.Offline, start, end);
        var client = await ClientAsync(lead);

        var unstaffed = await RequiredJsonAsync(await CloseAsync(client, unstaffedItem.Id));
        var entry = Entry(unstaffed, task.Id);
        entry.GetProperty("hasOperators").GetBoolean().Should().BeFalse();
        entry.GetProperty("isManualFallbackRequired").GetBoolean().Should().BeFalse();
        entry.GetProperty("manualGoodQuantity").GetInt32().Should().Be(0);
        entry.GetProperty("manualDefectQuantity").GetInt32().Should().Be(0);
        Shares(entry).Should().BeEmpty();

        var empty = await RequiredJsonAsync(await CloseAsync(client, emptyItem.Id));
        empty.GetProperty("entries").EnumerateArray().Should().BeEmpty();
        empty.GetProperty("timelines").EnumerateArray().Should().BeEmpty();
        empty.GetProperty("state").GetString().Should().Be("Closed");
    }

    [Fact]
    public async Task Close_BeforeStartForeignLeadAndMissingLeadAreRejected()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var future = (await SeedDayAsync(Item(lead.Id, now.AddHours(2), now.AddHours(4)))).Items.Single();
        var foreign = (await SeedDayAsync(Item(other.Id, now.AddHours(-2), now.AddHours(-1)))).Items.Single();
        var withoutLead = (await SeedDayAsync(Item(null, now.AddHours(-3), now.AddHours(-2),
            Interlocked.Increment(ref _positionOffset)))).Items.Single();
        var client = await ClientAsync(lead);

        var beforeStart = await CloseAsync(client, future.Id, confirmEarly: true);
        beforeStart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(beforeStart)).GetProperty("code").GetString().Should().Be("closeBeforeShiftStart");
        (await CloseAsync(client, foreign.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CloseAsync(client, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var managerClient = await ManagerClientAsync();
        var deactivatedManagers = await DeactivateActiveManagersAsync();
        try
        {
            var missingLead = await CloseAsync(managerClient, withoutLead.Id);
            missingLead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await JsonAsync(missingLead)).GetProperty("message").GetString().Should().Be("shiftLeadRequired");
        }
        finally
        {
            await RestoreManagersAsync(deactivatedManagers);
        }
    }

    [Fact]
    public async Task ConcurrentClose_ProducesOneClosedSnapshotAndOneConflictWithoutDuplicateShares()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 60);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, actualPartWeightGrams: 5, actualPackagingUnit: 20)));

        var responses = await Task.WhenAll(CloseAsync(client, item.Id), CloseAsync(client, item.Id));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosures.CountAsync(c => c.ShiftPlanItemId == item.Id)).Should().Be(1);
        (await db.ShiftClosureEntries.CountAsync(e => e.ShiftClosure.ShiftPlanItemId == item.Id)).Should().Be(1);
        var shares = await db.ShiftClosureOutputShares.AsNoTracking()
            .Where(s => s.ShiftClosureEntry.ShiftTaskId == task.Id).ToListAsync();
        shares.Should().ContainSingle().Which.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task CloseRacingIntervalMutation_SeesWholeIntervalOrMutationReturns409()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, packagingUnit: 20);
        await SeedDraftAsync(item.Id, lead.Id);
        var client = await ClientAsync(lead);
        var connectionString = await ConnectionStringAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const long key = unchecked((long)0x5754_5343_4845_4455);
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", key);
            await lockCommand.ExecuteNonQueryAsync();
        }

        var mutation = client.PostAsJsonAsync($"/api/shift-staffing/items/{item.Id}/operators", new
        {
            immId,
            operatorId = operatorUser.Id,
            assignedAt = start,
            unassignedAt = end
        });
        var close = CloseAsync(client, item.Id);
        var bothWaiting = false;
        try
        {
            for (var attempt = 0; attempt < 200 && !bothWaiting; attempt++)
            {
                if (mutation.IsCompleted || close.IsCompleted) break;
                await using var command = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
                    connection, transaction);
                bothWaiting = Convert.ToInt32(await command.ExecuteScalarAsync()) >= 2;
                if (!bothWaiting) await Task.Delay(25);
            }
        }
        finally
        {
            await transaction.CommitAsync();
        }
        bothWaiting.Should().BeTrue("mutation and close must serialize on ShiftScheduleLock");

        var mutationResponse = await mutation;
        var closeResponse = await close;
        mutationResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
        var body = await RequiredJsonAsync(closeResponse);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var assignmentExists = await db.ShiftImmOperatorAssignments.AnyAsync(a => a.ShiftPlanItemId == item.Id);
        assignmentExists.Should().Be(mutationResponse.StatusCode == HttpStatusCode.OK);
        var shares = Shares(Entry(body, task.Id));
        if (assignmentExists)
        {
            shares.Should().ContainSingle().Which.GetProperty("operatorId").GetString().Should().Be(operatorUser.Id);
            shares[0].GetProperty("quantity").GetInt32().Should().Be(0);
        }
        else
        {
            shares.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ClosedSnapshot_KeepsFrozenValuesButTimelineUsesCurrentOperatorIdentity()
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, planQuantity: 10, cavities: 2,
            partWeight: 5, packagingUnit: 20, actualQuantity: 7, defectQuantity: 2,
            actualMaterialWeight: 33, updatedAt: start.AddMinutes(5));
        await SeedRecipeAsync(task.Id);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
        await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 40);
        var openCycleId = await SeedCycleAsync(task, start.AddMinutes(50), cavities: 5,
            durationSeconds: 60, open: true);
        var beforeFacts = await TaskFactsAsync(task.Id);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutDraftAsync(client, item.Id,
            Manual(task.Id, 4.5m, null, 20, 1.5m, 7, 1)));

        var closeResponse = await CloseAsync(client, item.Id);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await closeResponse.Content.ReadAsStringAsync());
        var original = await closeResponse.Content.ReadAsStringAsync();
        (await TaskFactsAsync(task.Id)).Should().BeEquivalentTo(beforeFacts);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var openCycle = await db.ImmCycles.AsNoTracking().SingleAsync(c => c.Id == openCycleId);
            openCycle.EndTime.Should().BeNull();
        }

        await MutateSourcesAndCyclesAfterCloseAsync(task, openCycleId, start.AddMinutes(70), operatorUser.Id);
        var reread = await client.GetAsync($"/api/shift-closures/items/{item.Id}");
        reread.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await reread.Content.ReadAsStringAsync();

        using var originalBody = JsonDocument.Parse(original);
        using var currentBody = JsonDocument.Parse(current);
        var originalEntry = Entry(originalBody.RootElement, task.Id);
        var currentEntry = Entry(currentBody.RootElement, task.Id);
        currentEntry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(
            originalEntry.GetProperty("automaticOutputQuantity").GetInt32());
        currentEntry.GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(
            originalEntry.GetProperty("actualPartWeightGrams").GetDecimal());
        Shares(currentEntry).Single().GetProperty("operatorFullName").GetString().Should().Be(
            Shares(originalEntry).Single().GetProperty("operatorFullName").GetString());
        var timelineOperator = currentBody.RootElement.GetProperty("timelines").EnumerateArray().Single()
            .GetProperty("intervals").EnumerateArray().Single()
            .GetProperty("operators").EnumerateArray().Single();
        timelineOperator.GetProperty("operatorId").GetString().Should().Be(operatorUser.Id);
        timelineOperator.GetProperty("fullName").GetString().Should().Be("CURRENT OPERATOR AFTER CLOSE");
        timelineOperator.GetProperty("employeeId").GetString().Should().Be("CURRENT-EMPLOYEE");
        current.Should().NotBe(original,
            "только отображаемая идентичность литейщика timeline читается из текущего User");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Close_FallbackFlowFreezesEffectiveLeadForClosedReadPdfAndHistory(bool useDefault)
    {
        var position = Interlocked.Increment(ref _positionOffset);
        var defaultLead = await SeedUserAsync(UserRole.ShiftLead);
        var replacementLead = await SeedUserAsync(UserRole.ShiftLead);
        var previousDefault = await SetDefaultAsync(position, useDefault ? defaultLead.Id : null);
        TestUser? laterManager = null;
        try
        {
            var fallbackManager = useDefault ? null : await FirstActiveManagerAsync();
            var expectedLead = useDefault
                ? new LeadIdentity(defaultLead.Id, defaultLead.FullName, DateTime.MinValue)
                : fallbackManager!;
            var now = PastAnchor();
            var start = Utc(now.AddHours(-4));
            var end = Utc(now.AddHours(-2));
            var item = (await SeedDayAsync(Item(null, start, end, position))).Items.Single();
            var client = useDefault ? await ClientAsync(defaultLead) : await ManagerClientAsync();

            var draft = await RequiredJsonAsync(await PutDraftAsync(client, item.Id));
            draft.GetProperty("shiftLeadId").GetString().Should().Be(expectedLead.Id);
            draft.GetProperty("shiftLeadName").GetString().Should().Be(expectedLead.FullName);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await db.ShiftPlanItems.AsNoTracking().SingleAsync(i => i.Id == item.Id))
                    .ShiftLeadId.Should().BeNull("draft не фиксирует fallback");
            }

            var pending = await RequiredJsonAsync(await client.GetAsync("/api/shift-closures/pending"));
            var pendingItem = pending.EnumerateArray().Should().ContainSingle(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == item.Id).Subject;
            pendingItem.GetProperty("shiftLeadId").GetString().Should().Be(expectedLead.Id);
            pendingItem.GetProperty("shiftLeadName").GetString().Should().Be(expectedLead.FullName);

            var closeResponse = await CloseAsync(client, item.Id);
            closeResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                await closeResponse.Content.ReadAsStringAsync());
            var originalClosed = await closeResponse.Content.ReadAsStringAsync();
            using var closedDocument = JsonDocument.Parse(originalClosed);
            var closed = closedDocument.RootElement;
            closed.GetProperty("shiftLeadId").GetString().Should().Be(expectedLead.Id);
            closed.GetProperty("shiftLeadName").GetString().Should().Be(expectedLead.FullName);
            var closureId = closed.GetProperty("id").GetGuid();
            var originalPdf = PdfContent(await PdfBytesAsync(await client.GetAsync(
                $"/api/shift-closures/{closureId}/pdf")));

            if (useDefault)
            {
                await SetDefaultAsync(position, replacementLead.Id);
            }
            else
            {
                laterManager = await SeedUserAsync(UserRole.Manager);
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var user = await db.Users.SingleAsync(u => u.Id == laterManager.Id);
                user.CreatedAt = Utc(fallbackManager!.CreatedAt.AddTicks(-1));
                await db.SaveChangesAsync();
            }

            var reread = await RequiredJsonAsync(await client.GetAsync(
                $"/api/shift-closures/items/{item.Id}"));
            reread.GetProperty("shiftLeadId").GetString().Should().Be(expectedLead.Id);
            reread.GetProperty("shiftLeadName").GetString().Should().Be(expectedLead.FullName);
            reread.GetRawText().Should().Be(closed.GetRawText(),
                "поздняя смена default/Manager не меняет Closed DTO");
            PdfContent(await PdfBytesAsync(await client.GetAsync(
                $"/api/shift-closures/{closureId}/pdf"))).Should().Be(originalPdf);
            var pendingAfterClose = await RequiredJsonAsync(await client.GetAsync(
                "/api/shift-closures/pending"));
            pendingAfterClose.EnumerateArray().Should().NotContain(
                i => i.GetProperty("shiftPlanItemId").GetGuid() == item.Id);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await db.ShiftPlanItems.AsNoTracking().SingleAsync(i => i.Id == item.Id))
                    .ShiftLeadId.Should().Be(expectedLead.Id);
            }

            if (useDefault)
            {
                var originalHistory = await RequiredJsonAsync(await client.GetAsync(
                    "/api/shift-staffing/history"));
                originalHistory.EnumerateArray().Should().Contain(
                    i => i.GetProperty("itemId").GetGuid() == item.Id);
                var replacementClient = await ClientAsync(replacementLead);
                var replacementHistory = await RequiredJsonAsync(await replacementClient.GetAsync(
                    "/api/shift-staffing/history"));
                replacementHistory.EnumerateArray().Should().NotContain(
                    i => i.GetProperty("itemId").GetGuid() == item.Id);
            }
        }
        finally
        {
            await SetDefaultAsync(position, previousDefault);
            if (laterManager != null)
                await SetUserActiveAsync(laterManager.Id, false);
        }
    }

    [Fact]
    public async Task Close_ValidationFailureDoesNotFreezeDefaultLeadOrChangeDraft()
    {
        var position = Interlocked.Increment(ref _positionOffset);
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var previousDefault = await SetDefaultAsync(position, lead.Id);
        try
        {
            var now = PastAnchor();
            var start = Utc(now.AddHours(-4));
            var end = Utc(now.AddHours(-2));
            var operatorUser = await SeedUserAsync(UserRole.Operator);
            var immId = await SeedImmAsync();
            var item = (await SeedDayAsync(Item(null, start, end, position))).Items.Single();
            var task = await SeedTaskAsync(immId, start, end, packagingUnit: null);
            await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, 1, start, null);
            await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 60);
            var client = await ClientAsync(lead);
            await RequiredJsonAsync(await PutDraftAsync(client, item.Id, Manual(task.Id)));

            var response = await CloseAsync(client, item.Id);

            var errors = await FieldErrorsAsync(response);
            errors.Should().Contain(new FieldError(task.Id, "actualPartWeightGrams", "required"));
            errors.Should().Contain(new FieldError(task.Id, "actualPackagingUnit", "required"));
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftPlanItems.AsNoTracking().SingleAsync(i => i.Id == item.Id))
                .ShiftLeadId.Should().BeNull();
            var closure = await db.ShiftClosures.AsNoTracking().Include(c => c.Entries)
                .SingleAsync(c => c.ShiftPlanItemId == item.Id);
            closure.State.Should().Be(ShiftClosureState.Draft);
            closure.ClosedAt.Should().BeNull();
            var entry = closure.Entries.Single();
            entry.AutomaticOutputQuantity.Should().Be(0);
            entry.ActualPartWeightGrams.Should().BeNull();
            entry.ActualPackagingUnit.Should().BeNull();
        }
        finally
        {
            await SetDefaultAsync(position, previousDefault);
        }
    }

    private static object Manual(
        Guid taskId,
        decimal? actualPartWeightGrams = null,
        int? manualFullCycleSeconds = null,
        int? actualPackagingUnit = null,
        decimal? packagingPlaces = null,
        int? manualGoodQuantity = null,
        int? manualDefectQuantity = null) => new
    {
        shiftTaskId = taskId,
        actualPartWeightGrams,
        manualFullCycleSeconds,
        actualPackagingUnit,
        packagingPlaces,
        manualGoodQuantity,
        manualDefectQuantity
    };

    private static Task<HttpResponseMessage> PutDraftAsync(
        HttpClient client, Guid itemId, params object[] entries) =>
        client.PutAsJsonAsync($"/api/shift-closures/items/{itemId}/draft", new { entries });

    private static Task<HttpResponseMessage> CloseAsync(
        HttpClient client, Guid itemId, bool confirmEarly = false) =>
        client.PostAsJsonAsync($"/api/shift-closures/items/{itemId}/close", new
        {
            confirmEarlyClose = confirmEarly
        });

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user.Login, user.Password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"close_{role}_{suffix}";
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
            Name = $"CLOSE-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(
        string? leadId, DateTime start, DateTime end, int position = 1) => new()
    {
        Position = position,
        ShiftLeadId = leadId,
        StartAtUtc = Utc(start),
        EndAtUtc = Utc(end),
        TimeZoneId = "Europe/Moscow"
    };

    private async Task<ShiftPlanDay> SeedDayAsync(ShiftPlanItem item)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay
        {
            ShiftDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset))
        };
        day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return day;
    }

    private async Task<ShiftTask> SeedTaskAsync(
        Guid immId,
        DateTime start,
        DateTime end,
        TaskStatus status = TaskStatus.Completed,
        DateTime? plannedStartAt = null,
        int planQuantity = 10,
        int cavities = 1,
        decimal partWeight = 5,
        int? packagingUnit = 20,
        Guid? productId = null,
        int actualQuantity = 0,
        int defectQuantity = 0,
        decimal actualMaterialWeight = 0,
        DateTime? updatedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var planned = Utc(plannedStartAt ?? start);
        var local = schedule.ToLocal(planned);
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
            PlannedFullCycleSeconds = 60,
            SetupStartedAt = status == TaskStatus.Planned ? null : Utc(start),
            StartedAt = status == TaskStatus.Planned ? null : Utc(start),
            CompletedAt = status == TaskStatus.Planned ? null : Utc(end),
            ActualQuantity = actualQuantity,
            DefectQuantity = defectQuantity,
            ActualMaterialWeightGrams = actualMaterialWeight,
            UpdatedAt = Utc(updatedAt)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private async Task<Guid> SeedCycleAsync(
        ShiftTask task,
        DateTime endTime,
        int cavities,
        int durationSeconds,
        bool open = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var end = Utc(endTime);
        var cycle = new ImmCycle
        {
            ImmId = task.ImmId,
            TaskId = task.Id,
            MoldId = task.MoldId,
            StartTime = end.AddSeconds(-durationSeconds),
            EndTime = open ? null : end,
            DurationSeconds = durationSeconds,
            Cavities = cavities,
            IsSuccessful = true
        };
        db.ImmCycles.Add(cycle);
        await db.SaveChangesAsync();
        return cycle.Id;
    }

    private async Task SeedAssignmentAsync(
        Guid itemId, Guid immId, string operatorId, int slot,
        DateTime assignedAt, DateTime? unassignedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = itemId,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = slot,
            AssignedAt = Utc(assignedAt),
            UnassignedAt = Utc(unassignedAt),
            CreatedById = operatorId
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

    private async Task SeedRecipeAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var plastic = new Material
        {
            Type = MaterialType.Plastic,
            Article = $"PL-{suffix}",
            Name = $"CLOSE PLASTIC {suffix}",
            IsActive = true
        };
        var colorant = new Material
        {
            Type = MaterialType.Colorant,
            Article = $"CO-{suffix}",
            Name = $"CLOSE COLORANT {suffix}",
            ColorName = "Blue",
            IsActive = true
        };
        db.Materials.AddRange(plastic, colorant);
        db.ShiftTaskRecipeIngredients.AddRange(
            new ShiftTaskRecipeIngredient
            {
                ShiftTaskId = taskId,
                MaterialId = plastic.Id,
                OriginalMaterialId = plastic.Id,
                Percent = 100,
                SortOrder = 1
            },
            new ShiftTaskRecipeIngredient
            {
                ShiftTaskId = taskId,
                MaterialId = colorant.Id,
                OriginalMaterialId = colorant.Id,
                Percent = 1.5m,
                SortOrder = 2
            });
        await db.SaveChangesAsync();
    }

    private async Task SetProductSnapshotSourceAsync(Guid productId, string name, decimal runnerWeight)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var product = await db.Products.SingleAsync(p => p.Id == productId);
        product.Name = name;
        product.RunnerWeightGrams = runnerWeight;
        await db.SaveChangesAsync();
    }

    private async Task MakeUnstartedTaskDisappearAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = await db.ShiftTasks.SingleAsync(t => t.Id == taskId);
        task.Status = TaskStatus.Closed;
        task.ClosedAt = Utc(DateTime.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task SeedDraftAsync(Guid itemId, string leadId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftClosures.Add(new ShiftClosure
        {
            ShiftPlanItemId = itemId,
            State = ShiftClosureState.Draft,
            CreatedById = leadId
        });
        await db.SaveChangesAsync();
    }

    private async Task<TaskFacts> TaskFactsAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = await db.ShiftTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        return new TaskFacts(task.UpdatedAt, task.ActualQuantity, task.DefectQuantity,
            task.ActualMaterialWeightGrams, task.Status);
    }

    private async Task MutateSourcesAndCyclesAfterCloseAsync(
        ShiftTask task, Guid openCycleId, DateTime lateCycleEnd, string operatorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var current = await db.ShiftTasks.Include(t => t.Imm).Include(t => t.Product)
            .Include(t => t.RecipeIngredients).ThenInclude(i => i.Material)
            .SingleAsync(t => t.Id == task.Id);
        current.Imm.Name = "CURRENT IMM AFTER CLOSE";
        current.Product!.Name = "CURRENT PRODUCT AFTER CLOSE";
        current.Product.RunnerWeightGrams = 99;
        current.PartWeightGrams = 99;
        current.PackagingUnit = 99;
        current.Cavities = 3;
        var currentOperator = await db.Users.SingleAsync(u => u.Id == operatorId);
        currentOperator.FullName = "CURRENT OPERATOR AFTER CLOSE";
        currentOperator.EmployeeId = "CURRENT-EMPLOYEE";
        foreach (var ingredient in current.RecipeIngredients)
        {
            ingredient.Percent = 77;
            ingredient.Material!.Name = "CURRENT MATERIAL AFTER CLOSE";
        }
        var open = await db.ImmCycles.SingleAsync(c => c.Id == openCycleId);
        open.EndTime = Utc(lateCycleEnd.AddMinutes(-10));
        open.DurationSeconds = 60;
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = current.ImmId,
            TaskId = current.Id,
            MoldId = current.MoldId,
            StartTime = Utc(lateCycleEnd.AddSeconds(-60)),
            EndTime = Utc(lateCycleEnd),
            DurationSeconds = 60,
            Cavities = 7,
            IsSuccessful = true
        });
        var retro = new ImmCycle
        {
            ImmId = current.ImmId,
            TaskId = null,
            MoldId = current.MoldId,
            StartTime = Utc(lateCycleEnd.AddMinutes(1)),
            EndTime = Utc(lateCycleEnd.AddMinutes(2)),
            DurationSeconds = 60,
            Cavities = 9,
            IsSuccessful = true
        };
        db.ImmCycles.Add(retro);
        await db.SaveChangesAsync();
        retro.TaskId = current.Id;
        await db.SaveChangesAsync();
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
        return new LeadIdentity(manager.Id, manager.FullName, manager.CreatedAt);
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

    private async Task SetUserActiveAsync(string userId, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private static async Task<byte[]> PdfBytesAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static string PdfContent(byte[] bytes) => Regex.Replace(
        Encoding.Latin1.GetString(bytes),
        @"/(CreationDate|ModDate) \(D:[^)]*\)",
        "/$1 (D:volatile)");

    private async Task<string> ConnectionStringAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ControlDbContext>()
            .Database.GetDbConnection().ConnectionString;
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;

    private static async Task<List<FieldError>> FieldErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return body.GetProperty("errors").Deserialize<List<FieldError>>(JsonOptions)!;
    }

    private static JsonElement Entry(JsonElement body, Guid taskId) => body.GetProperty("entries")
        .EnumerateArray().Should().ContainSingle(e => e.GetProperty("taskId").GetGuid() == taskId).Subject;

    private static List<JsonElement> Shares(JsonElement entry) =>
        entry.GetProperty("outputShares").EnumerateArray().ToList();

    private static DateTime PastAnchor()
    {
        var value = DateTime.UtcNow.AddDays(-100 - 2 * Interlocked.Increment(ref _windowOffset));
        return new DateTime(value.Year, value.Month, value.Day,
            value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
}
