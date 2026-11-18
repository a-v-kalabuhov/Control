using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ShiftStaffing;

[Collection("Integration")]
public class ShiftStaffingIntervalApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 15000;

    private readonly IntegrationTestFactory _factory;

    public ShiftStaffingIntervalApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);

    [Fact]
    public async Task Assign_DefaultAndExplicitIntervals_UseShiftBoundsAndServerAudit()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var firstImm = await SeedImmAsync();
        var secondImm = await SeedImmAsync();
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(2));
        var client = await ClientAsync(lead);
        var before = DateTime.UtcNow;

        var defaulted = await RequiredAssignmentAsync(
            await AssignAsync(client, item.Id, firstImm, first.Id));
        var explicitStart = item.StartAtUtc.AddMinutes(20);
        var explicitEnd = item.StartAtUtc.AddMinutes(40);
        var explicitAssignment = await RequiredAssignmentAsync(
            await AssignAsync(client, item.Id, secondImm, second.Id, explicitStart, explicitEnd));
        var after = DateTime.UtcNow;

        defaulted.AssignedAt.Should().BeCloseTo(item.StartAtUtc, TimeSpan.FromSeconds(1));
        defaulted.UnassignedAt.Should().BeNull();
        defaulted.WorkStart.Should().BeCloseTo(item.StartAtUtc, TimeSpan.FromSeconds(1));
        defaulted.WorkEnd.Should().BeCloseTo(item.EndAtUtc, TimeSpan.FromSeconds(1));
        defaulted.CreatedById.Should().Be(lead.Id);
        defaulted.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        defaulted.UpdatedById.Should().BeNull();
        defaulted.UpdatedAt.Should().BeNull();
        explicitAssignment.AssignedAt.Should().BeCloseTo(explicitStart, TimeSpan.FromSeconds(1));
        explicitAssignment.UnassignedAt.Should().BeCloseTo(explicitEnd, TimeSpan.FromSeconds(1));
        explicitAssignment.CreatedById.Should().Be(lead.Id);
        explicitAssignment.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task MutationClientTimes_AreNormalizedToUtc_AndUnspecifiedIsRejected()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(2));
        var client = await ClientAsync(lead);
        var localWithOffset = new DateTimeOffset(item.StartAtUtc.AddMinutes(10), TimeSpan.Zero)
            .ToOffset(TimeSpan.FromHours(3));

        var normalizedResponse = await client.PostAsJsonAsync(
            $"/api/shift-staffing/items/{item.Id}/operators",
            new { immId, operatorId = @operator.Id, assignedAt = localWithOffset.ToString("O") });
        var normalized = await RequiredAssignmentAsync(normalizedResponse);
        normalized.AssignedAt.Should().BeCloseTo(
            localWithOffset.UtcDateTime, TimeSpan.FromSeconds(1));
        normalized.AssignedAt.Kind.Should().Be(DateTimeKind.Utc);

        var unspecified = item.StartAtUtc.AddMinutes(20).ToString("yyyy-MM-dd'T'HH:mm:ss");
        var response = await client.PostAsJsonAsync(
            $"/api/shift-staffing/items/{item.Id}/operators",
            new { immId = await SeedImmAsync(), operatorId = @operator.Id, assignedAt = unspecified });
        await ErrorAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignmentBounds_MustBeInsideShift_Ordered_AndEmptyOnlyForPreStartCancel()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(4));
        var client = await ClientAsync(lead);

        foreach (var (assignedAt, unassignedAt) in new[]
                 {
                     (item.StartAtUtc.AddMinutes(-1), (DateTime?)null),
                     (item.EndAtUtc.AddMinutes(1), (DateTime?)null),
                     (item.StartAtUtc, item.EndAtUtc.AddMinutes(1)),
                     (item.StartAtUtc.AddMinutes(30), item.StartAtUtc.AddMinutes(20)),
                     (item.StartAtUtc.AddMinutes(30), item.StartAtUtc.AddMinutes(30)),
                     (item.EndAtUtc, (DateTime?)null)
                 })
        {
            await ErrorAsync(await AssignAsync(
                client, item.Id, await SeedImmAsync(), @operator.Id, assignedAt, unassignedAt),
                HttpStatusCode.BadRequest);
        }

        var futureLead = await SeedUserAsync(UserRole.ShiftLead);
        var futureItem = await SeedItemAsync(futureLead.Id, now.AddHours(6), now.AddHours(8));
        var cancelled = await RequiredAssignmentAsync(await AssignAsync(
            await ClientAsync(futureLead), futureItem.Id, await SeedImmAsync(), @operator.Id,
            futureItem.StartAtUtc.AddMinutes(10), futureItem.StartAtUtc.AddMinutes(10)));
        cancelled.IsWorkIntervalEmpty.Should().BeTrue();
        (await AssignmentsAsync(futureItem.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Unassign_DefaultUsesActualWorkStartNowOrShiftEnd_AndRepeatIsRejected()
    {
        var now = DateTime.UtcNow;
        var futureLead = await SeedUserAsync(UserRole.ShiftLead);
        var currentLead = await SeedUserAsync(UserRole.ShiftLead);
        var historicalLead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var future = await SeedItemAsync(futureLead.Id, now.AddHours(2), now.AddHours(3));
        var current = await SeedItemAsync(currentLead.Id, now.AddHours(-1), now.AddHours(1));
        var historical = await SeedItemAsync(historicalLead.Id, now.AddHours(-5), now.AddHours(-4));
        var futureAssignment = await RequiredAssignmentAsync(await AssignAsync(
            await ClientAsync(futureLead), future.Id, await SeedImmAsync(), @operator.Id));
        var currentAssignment = await RequiredAssignmentAsync(await AssignAsync(
            await ClientAsync(currentLead), current.Id, await SeedImmAsync(), @operator.Id));
        var historicalAssignment = await SeedAssignmentAsync(
            historical, await SeedImmAsync(), @operator.Id, historical.StartAtUtc);

        var cancelled = await RequiredAssignmentAsync(
            await UnassignAsync(await ClientAsync(futureLead), futureAssignment.Id));
        var before = DateTime.UtcNow;
        var closedCurrent = await RequiredAssignmentAsync(
            await UnassignAsync(await ClientAsync(currentLead), currentAssignment.Id));
        var after = DateTime.UtcNow;
        var closedHistorical = await RequiredAssignmentAsync(
            await UnassignAsync(await ClientAsync(historicalLead), historicalAssignment.Id));

        cancelled.UnassignedAt.Should().BeCloseTo(future.StartAtUtc, TimeSpan.FromSeconds(1));
        cancelled.IsWorkIntervalEmpty.Should().BeTrue();
        closedCurrent.UnassignedAt.Should().NotBeNull();
        closedCurrent.UnassignedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        closedHistorical.UnassignedAt.Should().BeCloseTo(historical.EndAtUtc, TimeSpan.FromSeconds(1));
        closedHistorical.WorkEnd.Should().BeCloseTo(historical.EndAtUtc, TimeSpan.FromSeconds(1));
        closedCurrent.UpdatedById.Should().Be(currentLead.Id);
        closedCurrent.UpdatedAt.Should().NotBeNull();
        closedCurrent.CreatedAt.Should().BeCloseTo(currentAssignment.CreatedAt, TimeSpan.FromMilliseconds(1));
        closedCurrent.CreatedById.Should().Be(currentLead.Id);
        await ErrorAsync(await UnassignAsync(await ClientAsync(currentLead), currentAssignment.Id),
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unassign_ExplicitEndIsStored_AndMustNotPrecedeWorkStart()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(2));
        var assignment = await SeedAssignmentAsync(
            item, await SeedImmAsync(), @operator.Id, item.StartAtUtc.AddMinutes(10));
        var client = await ClientAsync(lead);
        var stored = (await AssignmentsAsync(item.Id)).Should().ContainSingle().Subject;

        await ErrorAsync(await UnassignAsync(client, assignment.Id, item.StartAtUtc.AddMinutes(9)),
            HttpStatusCode.BadRequest);
        await ErrorAsync(await UnassignAsync(client, assignment.Id, stored.AssignedAt),
            HttpStatusCode.BadRequest);
        var closed = await RequiredAssignmentAsync(
            await UnassignAsync(client, assignment.Id, item.StartAtUtc.AddMinutes(30)));

        closed.UnassignedAt.Should().BeCloseTo(item.StartAtUtc.AddMinutes(30), TimeSpan.FromSeconds(1));
        closed.IsWorkIntervalEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateInterval_ChangesBoundsAndAudit_ButPreservesIdentityAndCreation()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(2));
        var assignment = await SeedAssignmentAsync(
            item, await SeedImmAsync(), @operator.Id, item.StartAtUtc.AddMinutes(10));
        var client = await ClientAsync(lead);
        var originalCreatedAt = assignment.CreatedAt;
        var newStart = item.StartAtUtc.AddMinutes(20);
        var newEnd = item.StartAtUtc.AddMinutes(40);

        var updated = await RequiredAssignmentAsync(
            await UpdateIntervalAsync(client, assignment.Id, newStart, newEnd));

        updated.Id.Should().Be(assignment.Id);
        updated.AssignedAt.Should().BeCloseTo(newStart, TimeSpan.FromSeconds(1));
        updated.UnassignedAt.Should().BeCloseTo(newEnd, TimeSpan.FromSeconds(1));
        updated.CreatedById.Should().Be(lead.Id);
        updated.CreatedAt.Should().BeCloseTo(originalCreatedAt, TimeSpan.FromMilliseconds(1));
        updated.UpdatedById.Should().Be(lead.Id);
        updated.UpdatedAt.Should().NotBeNull();
        updated.UpdatedAt!.Value.Should().BeOnOrAfter(originalCreatedAt);

        await ErrorAsync(await UpdateIntervalAsync(client, assignment.Id, newStart, newStart),
            HttpStatusCode.BadRequest);
        await ErrorAsync(await UpdateIntervalAsync(
            client, assignment.Id, item.StartAtUtc.AddMinutes(-1), newEnd), HttpStatusCode.BadRequest);
        await ErrorAsync(await UpdateIntervalAsync(
            client, assignment.Id, newStart, item.EndAtUtc.AddMinutes(1)), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CapacityAndOperatorOverlap_UseWholeHalfOpenIntervals()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var third = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var otherImm = await SeedImmAsync();
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(6));
        var client = await ClientAsync(lead);
        var start = item.StartAtUtc;

        var firstRow = await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, first.Id, start, start.AddHours(2)));
        var secondRow = await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, second.Id, start, start.AddHours(2)));
        firstRow.Slot.Should().Be(1);
        secondRow.Slot.Should().Be(2);

        await ErrorAsync(await AssignAsync(
            client, item.Id, immId, third.Id, start.AddMinutes(30), start.AddHours(1)),
            HttpStatusCode.Conflict);
        await ErrorAsync(await AssignAsync(
            client, item.Id, immId, first.Id, start.AddHours(1), start.AddHours(3)),
            HttpStatusCode.Conflict);

        var sameOperatorOtherImm = await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, otherImm, first.Id, start, start.AddHours(2)));
        sameOperatorOtherImm.Slot.Should().Be(1);
        var sequentialSameOperator = await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, first.Id, start.AddHours(2), start.AddHours(4)));
        sequentialSameOperator.Slot.Should().Be(1, "полуоткрытые соседние интервалы не пересекаются");
    }

    [Fact]
    public async Task SequentialBusySlots_DoNotCauseAutomaticSplit()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var third = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = await SeedItemAsync(lead.Id, now.AddHours(-2), now.AddHours(6));
        var client = await ClientAsync(lead);
        var start = item.StartAtUtc;

        await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, first.Id, start, start.AddHours(2)));
        await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, first.Id, start.AddHours(4), start.AddHours(6)));
        await RequiredAssignmentAsync(await AssignAsync(
            client, item.Id, immId, second.Id, start.AddHours(1), start.AddHours(5)));

        await ErrorAsync(await AssignAsync(
            client, item.Id, immId, third.Id, start.AddMinutes(30), start.AddHours(5).AddMinutes(30)),
            HttpStatusCode.Conflict);
        (await AssignmentsAsync(item.Id)).Should().HaveCount(3);
        (await AssignmentsAsync(item.Id)).Should().NotContain(a => a.OperatorId == third.Id);
    }

    [Fact]
    public async Task EndedUnclosedOwnShift_AllowsAllMutationsWithoutChangingNearest()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var ended = await SeedItemAsync(lead.Id, now.AddHours(-6), now.AddHours(-4));
        var current = await SeedItemAsync(lead.Id, now.AddHours(-1), now.AddHours(1));
        var assignment = await SeedAssignmentAsync(
            ended, await SeedImmAsync(), @operator.Id, ended.StartAtUtc);
        var client = await ClientAsync(lead);

        var assigned = await RequiredAssignmentAsync(
            await AssignAsync(client, ended.Id, await SeedImmAsync(), @operator.Id));
        var updated = await RequiredAssignmentAsync(await UpdateIntervalAsync(
            client, assignment.Id, ended.StartAtUtc.AddMinutes(10), null));
        var unassigned = await RequiredAssignmentAsync(await UnassignAsync(client, assignment.Id));

        assigned.AssignedAt.Should().BeCloseTo(ended.StartAtUtc, TimeSpan.FromSeconds(1));
        updated.AssignedAt.Should().BeCloseTo(ended.StartAtUtc.AddMinutes(10), TimeSpan.FromSeconds(1));
        unassigned.UnassignedAt.Should().BeCloseTo(ended.EndAtUtc, TimeSpan.FromSeconds(1));
        var nearest = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/nearest"));
        nearest.GetProperty("item").GetProperty("itemId").GetGuid().Should().Be(current.Id);
        var history = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/history"));
        history.EnumerateArray().Single(i => i.GetProperty("itemId").GetGuid() == ended.Id)
            .GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndedUnclosedShift_AllowsManagerAndAdminMutations(bool admin)
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var ended = await SeedItemAsync(lead.Id, now.AddHours(-6), now.AddHours(-4));
        var assignment = await SeedAssignmentAsync(
            ended, await SeedImmAsync(), @operator.Id, ended.StartAtUtc);
        var client = admin ? await AdminClientAsync() : await ManagerClientAsync();

        await RequiredAssignmentAsync(
            await AssignAsync(client, ended.Id, await SeedImmAsync(), @operator.Id));
        await RequiredAssignmentAsync(await UpdateIntervalAsync(
            client, assignment.Id, ended.StartAtUtc.AddMinutes(10), null));
        await RequiredAssignmentAsync(await UnassignAsync(client, assignment.Id));

        var body = await RequiredJsonAsync(
            await client.GetAsync($"/api/shift-staffing/items/{ended.Id}"));
        body.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AlreadyClosedShift_RejectsMutationsWith400ShiftClosed()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var item = await SeedItemAsync(lead.Id, now.AddHours(-1), now.AddHours(1));
        var assignment = await SeedAssignmentAsync(
            item, await SeedImmAsync(), @operator.Id, item.StartAtUtc);
        await SeedClosureAsync(item, lead.Id, ShiftClosureState.Closed);
        var client = await ClientAsync(lead);

        await ErrorAsync(await AssignAsync(client, item.Id, await SeedImmAsync(), @operator.Id),
            HttpStatusCode.BadRequest, "shiftClosed");
        await ErrorAsync(await UnassignAsync(client, assignment.Id),
            HttpStatusCode.BadRequest, "shiftClosed");
        await ErrorAsync(await UpdateIntervalAsync(
            client, assignment.Id, item.StartAtUtc, item.StartAtUtc.AddMinutes(30)),
            HttpStatusCode.BadRequest, "shiftClosed");
        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        body.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ClosureCommittedWhileMutationWaitsForScheduleLock_Returns409()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = await SeedItemAsync(lead.Id, now.AddHours(-1), now.AddHours(1));
        await SeedClosureAsync(item, lead.Id, ShiftClosureState.Draft);
        var client = await ClientAsync(lead);
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<ControlDbContext>()
                .Database.GetDbConnection().ConnectionString;
        }

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

        var request = AssignAsync(client, item.Id, immId, @operator.Id);
        var waiting = false;
        for (var attempt = 0; attempt < 200 && !waiting && !request.IsCompleted; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
                connection, transaction);
            waiting = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            if (!waiting) await Task.Delay(25);
        }
        waiting.Should().BeTrue("mutation must read Draft before waiting for ShiftScheduleLock");

        await using (var closeCommand = new NpgsqlCommand("""
            UPDATE "ShiftClosures"
            SET "State" = 1, "ClosedAt" = now(), "ClosedById" = @leadId
            WHERE "ShiftPlanItemId" = @itemId
            """, connection, transaction))
        {
            closeCommand.Parameters.AddWithValue("leadId", lead.Id);
            closeCommand.Parameters.AddWithValue("itemId", item.Id);
            (await closeCommand.ExecuteNonQueryAsync()).Should().Be(1);
        }
        await transaction.CommitAsync();

        await ErrorAsync(await request, HttpStatusCode.Conflict);
        (await AssignmentsAsync(item.Id)).Should().BeEmpty();
    }

    private static Task<HttpResponseMessage> AssignAsync(
        HttpClient client, Guid itemId, Guid immId, string operatorId,
        DateTime? assignedAt = null, DateTime? unassignedAt = null) =>
        client.PostAsJsonAsync($"/api/shift-staffing/items/{itemId}/operators",
            new { immId, operatorId, assignedAt, unassignedAt });

    private static Task<HttpResponseMessage> UnassignAsync(
        HttpClient client, Guid assignmentId, DateTime? unassignedAt = null)
    {
        if (unassignedAt.HasValue)
            return client.PostAsJsonAsync(
                $"/api/shift-staffing/assignments/{assignmentId}/unassign", new { unassignedAt });
        return client.PostAsJsonAsync(
            $"/api/shift-staffing/assignments/{assignmentId}/unassign", new { });
    }

    private static Task<HttpResponseMessage> UpdateIntervalAsync(
        HttpClient client, Guid assignmentId, DateTime assignedAt, DateTime? unassignedAt) =>
        client.PutAsJsonAsync($"/api/shift-staffing/assignments/{assignmentId}/interval",
            new { assignedAt, unassignedAt });

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user.Login, user.Password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ClientAsync(string login, string password)
    {
        return ClientWithCredentialsAsync(login, password);
    }

    private async Task<HttpClient> ClientWithCredentialsAsync(string login, string password)
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
        var login = $"staffing_interval_{role}_{suffix}";
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

    private async Task<Guid> SeedImmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"STAFFING-INTERVAL-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private async Task<ShiftPlanItem> SeedItemAsync(string leadId, DateTime start, DateTime end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay
        {
            ShiftDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset))
        };
        var item = new ShiftPlanItem
        {
            Position = 1,
            ShiftLeadId = leadId,
            StartAtUtc = Utc(start),
            EndAtUtc = Utc(end),
            TimeZoneId = "Europe/Moscow"
        };
        day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return item;
    }

    private async Task<ShiftImmOperatorAssignment> SeedAssignmentAsync(
        ShiftPlanItem item, Guid immId, string operatorId, DateTime assignedAt,
        DateTime? unassignedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var assignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = Utc(assignedAt),
            UnassignedAt = unassignedAt.HasValue ? Utc(unassignedAt.Value) : null,
            CreatedById = item.ShiftLeadId!
        };
        db.ShiftImmOperatorAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private async Task SeedClosureAsync(
        ShiftPlanItem item, string createdById, ShiftClosureState state)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftClosures.Add(new ShiftClosure
        {
            ShiftPlanItemId = item.Id,
            State = state,
            CreatedById = createdById,
            ClosedById = state == ShiftClosureState.Closed ? createdById : null,
            ClosedAt = state == ShiftClosureState.Closed ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<ShiftImmOperatorAssignment>> AssignmentsAsync(Guid itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.ShiftImmOperatorAssignments.AsNoTracking()
            .Where(a => a.ShiftPlanItemId == itemId)
            .ToListAsync();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static async Task<ShiftOperatorAssignmentDto> RequiredAssignmentAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShiftOperatorAssignmentDto>(JsonOptions))!;
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static async Task ErrorAsync(
        HttpResponseMessage response, HttpStatusCode expected, string? messagePart = null)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, raw);
        var body = JsonSerializer.Deserialize<JsonElement>(raw, JsonOptions);
        var message = body.GetProperty("message").GetString();
        message.Should().NotBeNullOrWhiteSpace();
        if (messagePart != null) message.Should().Contain(messagePart);
    }
}
