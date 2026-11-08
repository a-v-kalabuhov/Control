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
public class ShiftStaffingMutationApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 5000;
    private static int _positionOffset = 10000;

    private readonly IntegrationTestFactory _factory;

    public ShiftStaffingMutationApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);

    [Fact]
    public async Task Assign_ActiveImmWithoutTask_UsesServerSlotsAndAllowsSameOperatorOnAnotherImm()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var firstImm = await SeedImmAsync(isActive: true);
        var secondImm = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var client = await ClientAsync(lead);
        var before = DateTime.UtcNow;

        var firstAssignment = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, firstImm, first.Id));
        var secondAssignment = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, firstImm, second.Id));
        var otherImmAssignment = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, secondImm, first.Id));
        var after = DateTime.UtcNow;

        firstAssignment.Slot.Should().Be(1);
        secondAssignment.Slot.Should().Be(2);
        otherImmAssignment.Slot.Should().Be(1);
        firstAssignment.UnassignedAt.Should().BeNull();
        firstAssignment.AssignedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        secondAssignment.AssignedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        firstAssignment.OperatorId.Should().Be(first.Id);
        otherImmAssignment.OperatorId.Should().Be(first.Id);

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var firstImmBody = Imm(body, firstImm);
        firstImmBody.GetProperty("tasks").EnumerateArray().Should().BeEmpty();
        firstImmBody.GetProperty("activeOperatorCount").GetInt32().Should().Be(2);
        Imm(body, secondImm).GetProperty("activeOperatorCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Assign_ThirdOperatorAndDuplicateActiveOperator_ReturnRussian400()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operators = new[]
        {
            await SeedUserAsync(UserRole.Operator),
            await SeedUserAsync(UserRole.Operator),
            await SeedUserAsync(UserRole.Operator)
        };
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var client = await ClientAsync(lead);
        await RequiredAssignmentAsync(await AssignAsync(client, item.Id, immId, operators[0].Id));
        await RequiredAssignmentAsync(await AssignAsync(client, item.Id, immId, operators[1].Id));

        await RussianErrorAsync(
            await AssignAsync(client, item.Id, immId, operators[2].Id), HttpStatusCode.BadRequest);
        await RussianErrorAsync(
            await AssignAsync(client, item.Id, immId, operators[0].Id), HttpStatusCode.BadRequest);

        var assignments = await AssignmentsAsync(item.Id);
        assignments.Should().HaveCount(2);
        assignments.Should().OnlyContain(a => a.UnassignedAt == null);
        assignments.Select(a => a.Slot).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Assign_ValidatesOperatorAndImm_AndAllowsOperatorWithoutLogin()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var archivedOperator = await SeedUserAsync(UserRole.Operator, isActive: false);
        var adjuster = await SeedUserAsync(UserRole.Adjuster);
        var withoutLogin = await SeedUserAsync(UserRole.Operator, withLogin: false);
        var activeImm = await SeedImmAsync(isActive: true);
        var archivedImm = await SeedImmAsync(isActive: false);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var client = await ClientAsync(lead);

        await RussianErrorAsync(await AssignAsync(client, item.Id, activeImm, archivedOperator.Id),
            HttpStatusCode.BadRequest);
        await RussianErrorAsync(await AssignAsync(client, item.Id, activeImm, Guid.NewGuid().ToString()),
            HttpStatusCode.NotFound);
        await RussianErrorAsync(await AssignAsync(client, item.Id, activeImm, adjuster.Id),
            HttpStatusCode.BadRequest);
        await RussianErrorAsync(await AssignAsync(client, item.Id, archivedImm, withoutLogin.Id),
            HttpStatusCode.BadRequest);
        await RussianErrorAsync(await AssignAsync(client, item.Id, Guid.NewGuid(), withoutLogin.Id),
            HttpStatusCode.NotFound);

        var assignment = await RequiredAssignmentAsync(
            await AssignAsync(client, item.Id, activeImm, withoutLogin.Id));
        assignment.OperatorId.Should().Be(withoutLogin.Id);
        (await AssignmentsAsync(item.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Assign_RequiresLead_RejectsHistoryAndStaleAccessWithRussianErrors()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var otherLead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var position = NextPosition();
        var noLead = (await SeedDayAsync(
            Item(position, null, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var historical = (await SeedDayAsync(
            Item(position, lead.Id, now.AddHours(-5), now.AddHours(-4)))).Items.Single();
        var nearest = (await SeedDayAsync(
            Item(position, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var later = (await SeedDayAsync(
            Item(position, lead.Id, now.AddHours(2), now.AddHours(3)))).Items.Single();
        var foreign = (await SeedDayAsync(
            Item(NextPosition(), otherLead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var leadClient = await ClientAsync(lead);
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();

        foreach (var client in new[] { leadClient, manager, admin })
        {
            await RussianErrorAsync(await AssignAsync(client, noLead.Id, immId, @operator.Id),
                HttpStatusCode.BadRequest);
            await RussianErrorAsync(await AssignAsync(client, historical.Id, immId, @operator.Id),
                HttpStatusCode.BadRequest);
        }
        await RussianErrorAsync(await AssignAsync(leadClient, foreign.Id, immId, @operator.Id),
            HttpStatusCode.Forbidden);
        await RussianErrorAsync(await AssignAsync(leadClient, later.Id, immId, @operator.Id),
            HttpStatusCode.Forbidden);
        await RussianErrorAsync(await AssignAsync(manager, later.Id, immId, @operator.Id),
            HttpStatusCode.Forbidden);
        await RussianErrorAsync(await AssignAsync(leadClient, Guid.NewGuid(), immId, @operator.Id),
            HttpStatusCode.NotFound);

        (await AssignmentsAsync(noLead.Id)).Should().BeEmpty();
        (await AssignmentsAsync(historical.Id)).Should().BeEmpty();
        (await AssignmentsAsync(nearest.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ManagerAndAdmin_CanAssignAndUnassignNearestShift()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(
            Item(NextPosition(), lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();

        var assigned = await RequiredAssignmentAsync(await AssignAsync(manager, item.Id, immId, @operator.Id));
        var unassigned = await RequiredAssignmentAsync(await UnassignAsync(admin, assigned.Id));

        unassigned.Id.Should().Be(assigned.Id);
        unassigned.UnassignedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnassignBeforeShift_PreservesEmptyWorkIntervalAndRepeatReturns400()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(
            Item(1, lead.Id, now.AddHours(2), now.AddHours(3)))).Items.Single();
        var client = await ClientAsync(lead);
        var assigned = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, immId, @operator.Id));
        var before = DateTime.UtcNow;

        var unassigned = await RequiredAssignmentAsync(await UnassignAsync(client, assigned.Id));
        var after = DateTime.UtcNow;

        unassigned.UnassignedAt.Should().NotBeNull();
        unassigned.UnassignedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        unassigned.IsWorkIntervalEmpty.Should().BeTrue();
        unassigned.WorkStart.Should().BeCloseTo(item.StartAtUtc, TimeSpan.FromSeconds(1));
        unassigned.WorkEnd.Should().BeCloseTo(unassigned.UnassignedAt.Value, TimeSpan.FromSeconds(1));
        await RussianErrorAsync(await UnassignAsync(client, assigned.Id), HttpStatusCode.BadRequest);

        var stored = (await AssignmentsAsync(item.Id)).Should().ContainSingle().Subject;
        stored.UnassignedAt.Should().BeCloseTo(unassigned.UnassignedAt.Value, TimeSpan.FromSeconds(1));
        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var read = Imm(body, immId).GetProperty("assignments").EnumerateArray().Should().ContainSingle().Subject;
        read.GetProperty("id").GetGuid().Should().Be(assigned.Id);
        read.GetProperty("isWorkIntervalEmpty").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task UnassignDuringShift_UsesServerTime_HistoricalActiveRowStaysOpenAndEndsAtShiftEnd()
    {
        var now = DateTime.UtcNow;
        var currentLead = await SeedUserAsync(UserRole.ShiftLead);
        var historicalLead = await SeedUserAsync(UserRole.ShiftLead);
        var currentOperator = await SeedUserAsync(UserRole.Operator);
        var historicalOperator = await SeedUserAsync(UserRole.Operator);
        var currentImm = await SeedImmAsync(isActive: true);
        var historicalImm = await SeedImmAsync(isActive: true);
        var current = (await SeedDayAsync(
            Item(1, currentLead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var historical = (await SeedDayAsync(
            Item(1, historicalLead.Id, now.AddHours(-5), now.AddHours(-4)))).Items.Single();
        var historicalAssignment = await SeedAssignmentAsync(historical, historicalImm, historicalOperator.Id,
            historical.StartAtUtc.AddMinutes(10));
        var currentClient = await ClientAsync(currentLead);
        var historicalClient = await ClientAsync(historicalLead);
        var assigned = await RequiredAssignmentAsync(
            await AssignAsync(currentClient, current.Id, currentImm, currentOperator.Id));
        var before = DateTime.UtcNow;

        var unassigned = await RequiredAssignmentAsync(await UnassignAsync(currentClient, assigned.Id));
        var after = DateTime.UtcNow;

        unassigned.UnassignedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        unassigned.IsWorkIntervalEmpty.Should().BeFalse();
        unassigned.WorkEnd.Should().BeCloseTo(unassigned.UnassignedAt.Value, TimeSpan.FromSeconds(1));

        await RussianErrorAsync(await UnassignAsync(historicalClient, historicalAssignment.Id),
            HttpStatusCode.BadRequest);
        var storedHistorical = (await AssignmentsAsync(historical.Id)).Should().ContainSingle().Subject;
        storedHistorical.UnassignedAt.Should().BeNull();
        var body = await RequiredJsonAsync(
            await historicalClient.GetAsync($"/api/shift-staffing/items/{historical.Id}"));
        var read = Imm(body, historicalImm).GetProperty("assignments").EnumerateArray()
            .Should().ContainSingle().Subject;
        read.GetProperty("unassignedAt").ValueKind.Should().Be(JsonValueKind.Null);
        read.GetProperty("workEnd").GetDateTime().Should().BeCloseTo(historical.EndAtUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReassignAfterUnassign_ReusesSlotWithNewRowAndPreservesHistory()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var first = await SeedUserAsync(UserRole.Operator);
        var second = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var client = await ClientAsync(lead);
        var old = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, immId, first.Id));
        var closed = await RequiredAssignmentAsync(await UnassignAsync(client, old.Id));

        var replacement = await RequiredAssignmentAsync(await AssignAsync(client, item.Id, immId, second.Id));

        replacement.Id.Should().NotBe(old.Id);
        replacement.Slot.Should().Be(1);
        replacement.UnassignedAt.Should().BeNull();
        var rows = await AssignmentsAsync(item.Id);
        rows.Should().HaveCount(2);
        var oldRow = rows.Single(a => a.Id == old.Id);
        oldRow.AssignedAt.Should().BeCloseTo(old.AssignedAt, TimeSpan.FromMilliseconds(1));
        oldRow.UnassignedAt.Should().BeCloseTo(closed.UnassignedAt!.Value, TimeSpan.FromMilliseconds(1));
        oldRow.Slot.Should().Be(1);
        rows.Single(a => a.Id == replacement.Id).UnassignedAt.Should().BeNull();

        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-staffing/items/{item.Id}"));
        var imm = Imm(body, immId);
        imm.GetProperty("assignments").EnumerateArray().Should().HaveCount(2);
        imm.GetProperty("activeOperatorCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task MutationEndpoints_RejectRolesOutsideShiftLeadManagerAdmin()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var assignmentId = Guid.NewGuid();

        foreach (var role in new[] { UserRole.Adjuster, UserRole.Observer, UserRole.Operator })
        {
            var user = await SeedUserAsync(role);
            var client = await ClientAsync(user);
            (await AssignAsync(client, item.Id, immId, @operator.Id)).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
            (await UnassignAsync(client, assignmentId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task ConcurrentAssignments_RowLockSerializesSlotSelectionAndCapacity()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operators = new[]
        {
            await SeedUserAsync(UserRole.Operator),
            await SeedUserAsync(UserRole.Operator),
            await SeedUserAsync(UserRole.Operator),
            await SeedUserAsync(UserRole.Operator)
        };
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var firstClient = await ClientAsync(lead);
        var secondClient = await ClientAsync(lead);

        var firstPair = await RunPairBehindParentRowLockAsync(item.Id,
            () => AssignAsync(firstClient, item.Id, immId, operators[0].Id),
            () => AssignAsync(secondClient, item.Id, immId, operators[1].Id));

        firstPair.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var successful = new List<ShiftOperatorAssignmentDto>();
        foreach (var response in firstPair)
            successful.Add(await RequiredAssignmentAsync(response));
        successful.Select(a => a.Slot).Should().BeEquivalentTo(new[] { 1, 2 });
        successful.Select(a => a.Id).Should().OnlyHaveUniqueItems();

        var secondPair = await RunPairBehindParentRowLockAsync(item.Id,
            () => AssignAsync(firstClient, item.Id, immId, operators[2].Id),
            () => AssignAsync(secondClient, item.Id, immId, operators[3].Id));

        secondPair.Should().OnlyContain(r =>
            r.StatusCode == HttpStatusCode.BadRequest || r.StatusCode == HttpStatusCode.Conflict);
        secondPair.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().BeLessOrEqualTo(1);
        foreach (var response in secondPair)
            await RussianErrorAsync(response, response.StatusCode);
        var rows = await AssignmentsAsync(item.Id);
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(a => a.UnassignedAt == null);
        rows.Select(a => a.OperatorId).Should().BeEquivalentTo(new[] { operators[0].Id, operators[1].Id });
        rows.Select(a => a.Slot).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task ConcurrentSameOperatorAssignment_CreatesExactlyOneActiveRow()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var firstClient = await ClientAsync(lead);
        var secondClient = await ClientAsync(lead);

        var responses = await RunPairBehindParentRowLockAsync(item.Id,
            () => AssignAsync(firstClient, item.Id, immId, @operator.Id),
            () => AssignAsync(secondClient, item.Id, immId, @operator.Id));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var failed = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        failed.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        await RussianErrorAsync(failed, failed.StatusCode);
        var rows = await AssignmentsAsync(item.Id);
        rows.Should().ContainSingle();
        rows[0].OperatorId.Should().Be(@operator.Id);
        rows[0].UnassignedAt.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentUnassign_ClosesAssignmentExactlyOnce()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync(isActive: true);
        var item = (await SeedDayAsync(Item(1, lead.Id, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var firstClient = await ClientAsync(lead);
        var secondClient = await ClientAsync(lead);
        var assignment = await RequiredAssignmentAsync(
            await AssignAsync(firstClient, item.Id, immId, @operator.Id));

        var responses = await RunPairBehindParentRowLockAsync(item.Id,
            () => UnassignAsync(firstClient, assignment.Id),
            () => UnassignAsync(secondClient, assignment.Id));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var failed = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        failed.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        await RussianErrorAsync(failed, failed.StatusCode);
        var success = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var closed = await RequiredAssignmentAsync(success);
        var rows = await AssignmentsAsync(item.Id);
        rows.Should().ContainSingle();
        rows[0].UnassignedAt.Should().NotBeNull();
        rows[0].UnassignedAt.Should().BeCloseTo(closed.UnassignedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    private async Task<HttpResponseMessage[]> RunPairBehindParentRowLockAsync(
        Guid itemId,
        Func<Task<HttpResponseMessage>> first,
        Func<Task<HttpResponseMessage>> second)
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
                         "SELECT 1 FROM \"ShiftPlanItems\" WHERE \"Id\" = @id FOR UPDATE",
                         connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", itemId);
            (await lockCommand.ExecuteScalarAsync()).Should().Be(1);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> StartTogether(Func<Task<HttpResponseMessage>> request)
        {
            await start.Task;
            return await request();
        }
        var firstRequest = StartTogether(first);
        var secondRequest = StartTogether(second);
        start.SetResult();

        var serializedWaiting = false;
        var lastParentWaiters = -1;
        var lastScheduleWaiters = -1;
        try
        {
            for (var attempt = 0; attempt < 200 && !serializedWaiting; attempt++)
            {
                if (firstRequest.IsCompleted || secondRequest.IsCompleted) break;
                await using var command = new NpgsqlCommand("""
                    SELECT
                      count(*) FILTER (
                        WHERE NOT granted AND (
                          locktype = 'transactionid' OR
                          (locktype = 'tuple' AND relation = '"ShiftPlanItems"'::regclass))),
                      count(*) FILTER (WHERE locktype = 'advisory' AND NOT granted)
                    FROM pg_locks
                    """, connection, transaction);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                var parentWaiters = reader.GetInt32(0);
                var scheduleWaiters = reader.GetInt32(1);
                lastParentWaiters = parentWaiters;
                lastScheduleWaiters = scheduleWaiters;
                serializedWaiting = parentWaiters >= 1 && scheduleWaiters >= 1;
                if (!serializedWaiting) await Task.Delay(25);
            }
        }
        finally
        {
            await transaction.CommitAsync();
        }

        var responses = await Task.WhenAll(firstRequest, secondRequest);
        serializedWaiting.Should().BeTrue(
            "both requests must pass initial validation; one waits on the externally locked item " +
            $"and the other on the schedule lock. Last waiters: parent={lastParentWaiters}, " +
            $"schedule={lastScheduleWaiters}");
        return responses;
    }

    private static Task<HttpResponseMessage> AssignAsync(
        HttpClient client, Guid itemId, Guid immId, string operatorId) =>
        client.PostAsJsonAsync($"/api/shift-staffing/items/{itemId}/operators",
            new { ImmId = immId, OperatorId = operatorId });

    private static Task<HttpResponseMessage> UnassignAsync(HttpClient client, Guid assignmentId) =>
        client.PostAsync($"/api/shift-staffing/assignments/{assignmentId}/unassign", null);

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user.Login, user.Password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ClientAsync(string login, string password) => ClientWithCredentialsAsync(login, password);

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

    private async Task<TestUser> SeedUserAsync(
        UserRole role, bool isActive = true, bool withLogin = true)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = withLogin ? $"staffing_mutation_{role}_{suffix}" : string.Empty;
        const string password = "Passw0rd!";
        var user = new User
        {
            UserName = withLogin ? login : null,
            NormalizedUserName = withLogin ? login.ToUpperInvariant() : null,
            Email = withLogin ? $"{login}@test.local" : null,
            NormalizedEmail = withLogin ? $"{login.ToUpperInvariant()}@TEST.LOCAL" : null,
            FullName = $"{role} {suffix}",
            EmployeeId = $"EMP-{suffix}"[..12],
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
        return new TestUser(user.Id, login, password);
    }

    private async Task<Guid> SeedImmAsync(bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"STAFFING-MUTATION-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = isActive
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(int position, string? leadId, DateTime start, DateTime end) => new()
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

    private async Task<ShiftImmOperatorAssignment> SeedAssignmentAsync(
        ShiftPlanItem item, Guid immId, string operatorId, DateTime assignedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var assignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = DateTime.SpecifyKind(assignedAt, DateTimeKind.Utc)
        };
        db.ShiftImmOperatorAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private async Task<List<ShiftImmOperatorAssignment>> AssignmentsAsync(Guid itemId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.ShiftImmOperatorAssignments.AsNoTracking()
            .Where(a => a.ShiftPlanItemId == itemId)
            .OrderBy(a => a.AssignedAt).ThenBy(a => a.Id)
            .ToListAsync();
    }

    private static int NextPosition() => Interlocked.Increment(ref _positionOffset);

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

    private static async Task RussianErrorAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, raw);
        var body = JsonSerializer.Deserialize<JsonElement>(raw, JsonOptions);
        var message = body.GetProperty("message").GetString();
        message.Should().NotBeNullOrWhiteSpace();
        message.Should().MatchRegex(@"\p{IsCyrillic}");
    }

    private static JsonElement Imm(JsonElement item, Guid immId) => item.GetProperty("imms")
        .EnumerateArray().Should().ContainSingle(i => i.GetProperty("immId").GetGuid() == immId).Subject;
}
