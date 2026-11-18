using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
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
public class ShiftClosureDraftApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 7000;
    private readonly IntegrationTestFactory _factory;

    public ShiftClosureDraftApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);
    private sealed record FieldError(Guid TaskId, string Field, string Code);

    [Fact]
    public async Task FirstPut_CreatesOneDraftWithServerForeignKeysAndAuditAndRepeatDoesNotDuplicate()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var supplied = await SeedTaskAsync(immId, start, end);
        var notSupplied = await SeedTaskAsync(immId, start, end);
        var client = await ClientAsync(lead);

        var firstResponse = await PutAsync(client, item.Id,
            Manual(supplied.Id, 4.25m, 57, 24, 2.50m, 8, 1));
        var first = await RequiredJsonAsync(firstResponse);

        first.GetProperty("state").GetString().Should().Be("Draft");
        first.GetProperty("createdById").GetString().Should().Be(lead.Id);
        first.GetProperty("createdAt").GetDateTime().Should().BeCloseTo(now, TimeSpan.FromSeconds(5));
        Entry(first, supplied.Id).GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(4.25m);
        Entry(first, notSupplied.Id).GetProperty("actualPartWeightGrams").ValueKind.Should()
            .Be(JsonValueKind.Null);
        var closureId = first.GetProperty("id").GetGuid();
        Guid entryId;
        DateTime createdAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var closure = await db.ShiftClosures.AsNoTracking()
                .SingleAsync(c => c.ShiftPlanItemId == item.Id);
            closure.Id.Should().Be(closureId);
            closure.State.Should().Be(ShiftClosureState.Draft);
            closure.CreatedById.Should().Be(lead.Id);
            closure.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
            createdAt = closure.CreatedAt;
            var entry = await db.ShiftClosureEntries.AsNoTracking()
                .SingleAsync(e => e.ShiftClosureId == closureId);
            entryId = entry.Id;
            entry.ShiftTaskId.Should().Be(supplied.Id);
            entry.ImmId.Should().Be(immId);
            entry.ActualPartWeightGrams.Should().Be(4.25m);
            entry.ManualFullCycleSeconds.Should().Be(57);
            entry.ActualPackagingUnit.Should().Be(24);
            entry.PackagingPlaces.Should().Be(2.50m);
            entry.ManualGoodQuantity.Should().Be(8);
            entry.ManualDefectQuantity.Should().Be(1);
            entry.ImmNameSnapshot.Should().BeEmpty();
            entry.ProductNameSnapshot.Should().BeEmpty();
            entry.NormPartWeightGrams.Should().Be(0);
            entry.AutomaticOutputQuantity.Should().Be(0);
            (await db.ShiftClosureEntries.AnyAsync(e => e.ShiftTaskId == notSupplied.Id)).Should().BeFalse();
        }

        var second = await RequiredJsonAsync(await PutAsync(client, item.Id,
            Manual(supplied.Id, 4.75m, 58, 25, 3.25m, 9, 2)));

        second.GetProperty("id").GetGuid().Should().Be(closureId);
        second.GetProperty("createdAt").GetDateTime().Should().Be(createdAt);
        Entry(second, supplied.Id).GetProperty("manualGoodQuantity").GetInt32().Should().Be(9);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await verifyDb.ShiftClosures.CountAsync(c => c.ShiftPlanItemId == item.Id)).Should().Be(1);
        var updated = await verifyDb.ShiftClosureEntries.SingleAsync(e => e.ShiftClosureId == closureId);
        updated.Id.Should().Be(entryId);
        updated.ManualGoodQuantity.Should().Be(9);
        updated.ManualDefectQuantity.Should().Be(2);
    }

    [Fact]
    public async Task Put_ReplacesAllSixFieldsForSuppliedTaskWithoutClearingAbsentTask()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var replaced = await SeedTaskAsync(immId, start, end);
        var preserved = await SeedTaskAsync(immId, start, end);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutAsync(client, item.Id,
            Manual(replaced.Id, 4, 50, 20, 1, 5, 1),
            Manual(preserved.Id, 6, 70, 30, 2, 7, 2)));

        var body = await RequiredJsonAsync(await PutAsync(client, item.Id, Manual(replaced.Id)));

        foreach (var field in ManualFields)
            Entry(body, replaced.Id).GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);
        Entry(body, preserved.Id).GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(6);
        Entry(body, preserved.Id).GetProperty("manualFullCycleSeconds").GetInt32().Should().Be(70);
        Entry(body, preserved.Id).GetProperty("actualPackagingUnit").GetInt32().Should().Be(30);
        Entry(body, preserved.Id).GetProperty("packagingPlaces").GetDecimal().Should().Be(2);
        Entry(body, preserved.Id).GetProperty("manualGoodQuantity").GetInt32().Should().Be(7);
        Entry(body, preserved.Id).GetProperty("manualDefectQuantity").GetInt32().Should().Be(2);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosureEntries.CountAsync(e => e.ShiftClosure.ShiftPlanItemId == item.Id)).Should().Be(2);
    }

    [Fact]
    public async Task GetAfterPut_RecomputesAutomaticValuesWithoutChangingSavedManualValues()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end);
        await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 40);
        await SeedCycleAsync(task, start.AddMinutes(50), cavities: 1, durationSeconds: 60);
        var client = await ClientAsync(lead);
        var saved = await RequiredJsonAsync(await PutAsync(client, item.Id,
            Manual(task.Id, 4.5m, 55, 25, 1.25m, 8, 2)));
        Entry(saved, task.Id).GetProperty("automaticOutputQuantity").GetInt32().Should().Be(3);
        Entry(saved, task.Id).GetProperty("automaticAverageFullCycleSeconds").GetDouble().Should().Be(50);

        await SeedCycleAsync(task, start.AddMinutes(70), cavities: 4, durationSeconds: 20);
        var body = await RequiredJsonAsync(await client.GetAsync($"/api/shift-closures/items/{item.Id}"));

        var entry = Entry(body, task.Id);
        entry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(7);
        entry.GetProperty("automaticAverageFullCycleSeconds").GetDouble().Should().Be(40);
        entry.GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(4.5m);
        entry.GetProperty("manualFullCycleSeconds").GetInt32().Should().Be(55);
        entry.GetProperty("actualPackagingUnit").GetInt32().Should().Be(25);
        entry.GetProperty("packagingPlaces").GetDecimal().Should().Be(1.25m);
        entry.GetProperty("manualGoodQuantity").GetInt32().Should().Be(8);
        entry.GetProperty("manualDefectQuantity").GetInt32().Should().Be(2);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var persisted = await db.ShiftClosureEntries.AsNoTracking().SingleAsync(e => e.ShiftTaskId == task.Id);
        persisted.ManualGoodQuantity.Should().Be(8);
        persisted.AutomaticOutputQuantity.Should().Be(0, "draft stores no automatic snapshot");
    }

    [Fact]
    public async Task Put_NonmemberNonexistentTaskOrMissingItemReturns404WithoutWriting()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var member = await SeedTaskAsync(immId, start, end);
        var nonmember = await SeedTaskAsync(immId, now.AddHours(-10), now.AddHours(-9));
        var client = await ClientAsync(lead);

        (await PutAsync(client, item.Id, Manual(nonmember.Id, 4))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutAsync(client, item.Id, Manual(Guid.NewGuid(), 4))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PutAsync(client, Guid.NewGuid(), Manual(member.Id, 4))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosures.AnyAsync(c => c.ShiftPlanItemId == item.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Put_ClosedReturns400ForeignLeadAndDisallowedRolesReturn403()
    {
        var now = DateTime.UtcNow;
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var closedItem = (await SeedDayAsync(Item(lead.Id, now.AddHours(-5), now.AddHours(-4)))).Items.Single();
        var closedTask = await SeedTaskAsync(immId, closedItem.StartAtUtc, closedItem.EndAtUtc);
        await SeedClosureAsync(closedItem, lead.Id, ShiftClosureState.Closed);
        var foreignItem = (await SeedDayAsync(Item(other.Id, now.AddHours(-3), now.AddHours(-2)))).Items.Single();
        var foreignTask = await SeedTaskAsync(immId, foreignItem.StartAtUtc, foreignItem.EndAtUtc);
        var leadClient = await ClientAsync(lead);

        var closed = await PutAsync(leadClient, closedItem.Id, Manual(closedTask.Id, 4));
        closed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(closed)).GetProperty("message").GetString().Should().Be("shiftClosed");
        (await PutAsync(leadClient, foreignItem.Id, Manual(foreignTask.Id, 4))).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);

        foreach (var role in new[] { UserRole.Operator, UserRole.Adjuster, UserRole.Observer })
        {
            var user = await SeedUserAsync(role);
            var client = await ClientAsync(user);
            (await PutAsync(client, foreignItem.Id, Manual(foreignTask.Id, 4))).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden, role.ToString());
        }
    }

    [Fact]
    public async Task Put_DraftAcceptsIncompleteOfflineFallback()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, start);
        await SeedStatusAsync(immId, ImmStatus.Offline, start, end);
        var client = await ClientAsync(lead);

        var body = await RequiredJsonAsync(await PutAsync(client, item.Id, Manual(task.Id)));

        var entry = Entry(body, task.Id);
        entry.GetProperty("isManualFallbackRequired").GetBoolean().Should().BeTrue();
        foreach (var field in ManualFields)
            entry.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Null, field);
    }

    [Fact]
    public async Task Put_InvalidRangesReturnTaskFieldErrorsAndWholeBatchIsAtomic()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var valid = await SeedTaskAsync(immId, start, end);
        var invalid = await SeedTaskAsync(immId, start, end);
        var invalidPlaces = await SeedTaskAsync(immId, start, end);
        var client = await ClientAsync(lead);
        await RequiredJsonAsync(await PutAsync(client, item.Id,
            Manual(valid.Id, 4, 50, 20, 1, 5, 1),
            Manual(invalid.Id, 5, 60, 21, 2, 6, 2),
            Manual(invalidPlaces.Id, 6, 70, 22, 3, 7, 3)));

        var response = await PutAsync(client, item.Id,
            Manual(valid.Id, 9, 90, 90, 9, 9, 9),
            Manual(invalid.Id, -1, 0, 0, -1, -1, -1),
            Manual(invalidPlaces.Id, packagingPlaces: 1.234m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await JsonAsync(response);
        var errors = body.GetProperty("errors").Deserialize<List<FieldError>>(JsonOptions)!;
        errors.Should().BeEquivalentTo(new[]
        {
            new FieldError(invalid.Id, "actualPartWeightGrams", "mustBePositive"),
            new FieldError(invalid.Id, "manualFullCycleSeconds", "mustBePositive"),
            new FieldError(invalid.Id, "actualPackagingUnit", "mustBePositive"),
            new FieldError(invalid.Id, "packagingPlaces", "mustBeNonNegative"),
            new FieldError(invalid.Id, "manualGoodQuantity", "mustBeNonNegative"),
            new FieldError(invalid.Id, "manualDefectQuantity", "mustBeNonNegative"),
            new FieldError(invalidPlaces.Id, "packagingPlaces", "maxTwoDecimalPlaces")
        });
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var entries = await db.ShiftClosureEntries.AsNoTracking()
            .Where(e => e.ShiftClosure.ShiftPlanItemId == item.Id).ToListAsync();
        entries.Should().HaveCount(3);
        entries.Single(e => e.ShiftTaskId == valid.Id).ManualGoodQuantity.Should().Be(5);
        entries.Single(e => e.ShiftTaskId == invalid.Id).ManualGoodQuantity.Should().Be(6);
        entries.Single(e => e.ShiftTaskId == invalidPlaces.Id).PackagingPlaces.Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentFirstPuts_CreateOneDraftAndLoserGets409()
    {
        var now = DateTime.UtcNow;
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end);
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
        await using (var lockCommand = new NpgsqlCommand(
            "LOCK TABLE \"ShiftPlanItems\" IN EXCLUSIVE MODE", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync();

        var requests = Task.WhenAll(
            PutAsync(client, item.Id, Manual(task.Id, 4, manualGoodQuantity: 10)),
            PutAsync(client, item.Id, Manual(task.Id, 5, manualGoodQuantity: 20)));
        var serializedWaiting = false;
        try
        {
            for (var attempt = 0; attempt < 200 && !serializedWaiting; attempt++)
            {
                if (requests.IsCompleted) break;
                await using var command = new NpgsqlCommand("""
                    SELECT
                      count(*) FILTER (WHERE NOT granted AND relation = '"ShiftPlanItems"'::regclass),
                      count(*) FILTER (WHERE locktype = 'advisory' AND NOT granted)
                    FROM pg_locks
                    """, connection, transaction);
                await using var reader = await command.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                serializedWaiting = reader.GetInt32(0) >= 1 && reader.GetInt32(1) >= 1;
                if (!serializedWaiting) await Task.Delay(25);
            }
        }
        finally
        {
            await transaction.CommitAsync();
        }

        serializedWaiting.Should().BeTrue("both requests must read the absent draft before serialized writes");
        var responses = await requests;
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var winner = await RequiredJsonAsync(responses.Single(r => r.StatusCode == HttpStatusCode.OK));
        var expectedGood = Entry(winner, task.Id).GetProperty("manualGoodQuantity").GetInt32();
        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosures.CountAsync(c => c.ShiftPlanItemId == item.Id)).Should().Be(1);
        var entry = await db.ShiftClosureEntries.SingleAsync(e => e.ShiftTaskId == task.Id);
        entry.ManualGoodQuantity.Should().Be(expectedGood);
    }

    [Fact]
    public void DraftRequest_ContainsOnlyTaskIdAndSixManualFields()
    {
        var assembly = typeof(ShiftClosureDto).Assembly;
        var request = assembly.GetType(
            "Wintime.Control.Core.DTOs.ShiftClosures.SaveShiftClosureDraftRequest");
        var input = assembly.GetType(
            "Wintime.Control.Core.DTOs.ShiftClosures.ShiftClosureManualEntryInput");
        request.Should().NotBeNull();
        input.Should().NotBeNull();

        PublicProperties(request!).Should().BeEquivalentTo(new[] { "Entries" });
        PublicProperties(input!).Should().BeEquivalentTo(new[]
        {
            "ShiftTaskId", "ActualPartWeightGrams", "ManualFullCycleSeconds", "ActualPackagingUnit",
            "PackagingPlaces", "ManualGoodQuantity", "ManualDefectQuantity"
        });
    }

    private static readonly string[] ManualFields =
    {
        "actualPartWeightGrams", "manualFullCycleSeconds", "actualPackagingUnit",
        "packagingPlaces", "manualGoodQuantity", "manualDefectQuantity"
    };

    private static string[] PublicProperties(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Select(p => p.Name).ToArray();

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

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client, Guid itemId, params object[] entries) =>
        client.PutAsJsonAsync($"/api/shift-closures/items/{itemId}/draft", new { entries });

    private async Task<HttpClient> ClientAsync(TestUser user)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user.Login, user.Password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"draft_{role}_{suffix}";
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
            Name = $"DRAFT-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(string? leadId, DateTime start, DateTime end) => new()
    {
        Position = 1,
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

    private async Task<ShiftTask> SeedTaskAsync(Guid immId, DateTime start, DateTime end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var local = schedule.ToLocal(Utc(start));
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
            SetupStartedAt = Utc(start),
            StartedAt = Utc(start),
            CompletedAt = Utc(end)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private async Task SeedCycleAsync(
        ShiftTask task, DateTime endTime, int cavities, int durationSeconds)
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
            EndTime = end,
            DurationSeconds = durationSeconds,
            Cavities = cavities,
            IsSuccessful = true
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAssignmentAsync(Guid itemId, Guid immId, string operatorId, DateTime start)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = itemId,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = Utc(start),
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

    private async Task SeedClosureAsync(ShiftPlanItem item, string leadId, ShiftClosureState state)
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

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;

    private static JsonElement Entry(JsonElement body, Guid taskId) => body.GetProperty("entries")
        .EnumerateArray().Should().ContainSingle(e => e.GetProperty("taskId").GetGuid() == taskId).Subject;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
