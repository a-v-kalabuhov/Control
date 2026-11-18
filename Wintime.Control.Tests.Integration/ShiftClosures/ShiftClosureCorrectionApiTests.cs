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
public class ShiftClosureCorrectionApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 13000;
    private static int _windowOffset;
    private readonly IntegrationTestFactory _factory;

    public ShiftClosureCorrectionApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(
        string Id, string Login, string Password, string FullName, string? EmployeeId);

    private sealed record FieldError(Guid TaskId, string Field, string Code);

    private sealed record ClosedFixture(
        TestUser Lead,
        ShiftPlanItem Item,
        Guid ClosureId,
        IReadOnlyList<ShiftTask> Tasks);

    [Fact]
    public async Task Correction_ChangesOnlyManualFieldsAndAppendsServerAuditWithActualChanges()
    {
        var fixture = await SeedClosedAsync();
        var task = fixture.Tasks.Single();
        var manager = await ClientAsync("test_manager", "Manager123!");
        var before = await RequiredJsonAsync(await manager.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"));
        var beforeEntry = Entry(before, task.Id);
        var beforeShares = beforeEntry.GetProperty("outputShares").GetRawText();
        var beforeTimelines = before.GetProperty("timelines").GetRawText();
        var beforeClosedAt = before.GetProperty("closedAt").GetDateTime();
        var beforeClosedBy = before.GetProperty("closedById").GetString();
        var beforeCreatedAt = before.GetProperty("createdAt").GetDateTime();
        var beforePlan = beforeEntry.GetProperty("shiftPlanQuantity").GetInt32();
        var beforeAutomatic = beforeEntry.GetProperty("automaticOutputQuantity").GetInt32();
        var beforeTask = await TaskSnapshotAsync(task.Id);

        var response = await PatchAsync(manager, fixture.ClosureId, "  Исправление по акту  ",
            Manual(task.Id, 5.5m, 65, 24, 1.75m, 8, 1));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var after = await RequiredJsonAsync(await manager.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"));
        after.GetProperty("state").GetString().Should().Be("Closed");
        after.GetProperty("closedAt").GetDateTime().Should().Be(beforeClosedAt);
        after.GetProperty("closedById").GetString().Should().Be(beforeClosedBy);
        after.GetProperty("createdAt").GetDateTime().Should().Be(beforeCreatedAt);
        after.GetProperty("timelines").GetRawText().Should().Be(beforeTimelines);
        var entry = Entry(after, task.Id);
        entry.GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(5.5m);
        entry.GetProperty("manualFullCycleSeconds").GetInt32().Should().Be(65);
        entry.GetProperty("actualPackagingUnit").GetInt32().Should().Be(24);
        entry.GetProperty("packagingPlaces").GetDecimal().Should().Be(1.75m);
        entry.GetProperty("manualGoodQuantity").GetInt32().Should().Be(8);
        entry.GetProperty("manualDefectQuantity").GetInt32().Should().Be(1);
        entry.GetProperty("shiftPlanQuantity").GetInt32().Should().Be(beforePlan);
        entry.GetProperty("automaticOutputQuantity").GetInt32().Should().Be(beforeAutomatic);
        entry.GetProperty("outputShares").GetRawText().Should().Be(beforeShares);
        (await TaskSnapshotAsync(task.Id)).Should().BeEquivalentTo(beforeTask);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var audit = await db.ShiftClosureCorrections.AsNoTracking()
            .SingleAsync(c => c.ShiftClosureId == fixture.ClosureId);
        audit.Reason.Should().Be("Исправление по акту");
        audit.ChangedById.Should().Be(await UserIdAsync("test_manager"));
        audit.ChangedAt.Kind.Should().Be(DateTimeKind.Utc);
        audit.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        var changes = JsonDocument.Parse(audit.ChangesJson).RootElement.EnumerateArray().ToList();
        changes.Should().HaveCount(5, "неизменный manualDefectQuantity не должен попадать в аудит");
        changes.Select(c => c.GetProperty("taskId").GetGuid()).Should().OnlyContain(id => id == task.Id);
        changes.Select(c => c.GetProperty("field").GetString()).Should().BeEquivalentTo(new[]
        {
            "actualPartWeightGrams", "manualFullCycleSeconds", "actualPackagingUnit",
            "packagingPlaces", "manualGoodQuantity"
        });
        var good = changes.Single(c => c.GetProperty("field").GetString() == "manualGoodQuantity");
        good.GetProperty("oldValue").GetInt32().Should().Be(4);
        good.GetProperty("newValue").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task CorrectionHistory_IsChronologicalAndEnforcesRolesAndLeadOwnership()
    {
        var fixture = await SeedClosedAsync();
        var task = fixture.Tasks.Single();
        var manager = await ClientAsync("test_manager", "Manager123!");
        var admin = await SeedUserAsync(UserRole.Admin);
        var adminClient = await ClientAsync(admin);
        (await PatchAsync(manager, fixture.ClosureId, "Первая правка",
            Manual(task.Id, manualGoodQuantity: 5))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PatchAsync(adminClient, fixture.ClosureId, "Вторая правка",
            Manual(task.Id, manualGoodQuantity: 6))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var history = await RequiredJsonAsync(await (await ClientAsync(fixture.Lead)).GetAsync(
            $"/api/shift-closures/{fixture.ClosureId}/corrections"));
        var rows = history.EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("reason").GetString()).Should().Equal("Первая правка", "Вторая правка");
        rows[0].GetProperty("changedAt").GetDateTime().Should()
            .BeOnOrBefore(rows[1].GetProperty("changedAt").GetDateTime());
        rows[0].GetProperty("changedByName").GetString().Should().Be("Test Manager");
        rows[1].GetProperty("changedByName").GetString().Should().Be(admin.FullName);
        rows[1].GetProperty("changes").EnumerateArray().Single()
            .GetProperty("newValue").GetInt32().Should().Be(6);
        (await manager.GetAsync($"/api/shift-closures/{fixture.ClosureId}/corrections"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminClient.GetAsync($"/api/shift-closures/{fixture.ClosureId}/corrections"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var foreignLead = await SeedUserAsync(UserRole.ShiftLead);
        (await (await ClientAsync(foreignLead)).GetAsync(
            $"/api/shift-closures/{fixture.ClosureId}/corrections")).StatusCode.Should()
            .Be(HttpStatusCode.Forbidden);
        (await (await ClientAsync(fixture.Lead)).PatchAsJsonAsync(
            $"/api/shift-closures/{fixture.ClosureId}/corrections", new
            {
                reason = "lead forbidden",
                entries = new[] { Manual(task.Id, manualGoodQuantity: 7) }
            })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var role in new[] { UserRole.Operator, UserRole.Adjuster, UserRole.Observer })
        {
            var client = await ClientAsync(await SeedUserAsync(role));
            (await client.GetAsync($"/api/shift-closures/{fixture.ClosureId}/corrections"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
            (await client.PatchAsJsonAsync($"/api/shift-closures/{fixture.ClosureId}/corrections", new
            {
                reason = "forbidden",
                entries = new[] { Manual(task.Id, manualGoodQuantity: 7) }
            })).StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }
    }

    [Fact]
    public async Task Correction_RevalidatesAgainstFrozenSnapshotAndInvalidSecondRowRollsBackBatchAndAudit()
    {
        var fixture = await SeedClosedAsync(taskCount: 2);
        var first = fixture.Tasks[0];
        var second = fixture.Tasks[1];
        var manager = await ClientAsync("test_manager", "Manager123!");
        var beforeFirst = await ManualSnapshotAsync(first.Id);
        var beforeSecond = await ManualSnapshotAsync(second.Id);

        // После закрытия актуальные назначения и исходники заданий не должны влиять на проверку снимка.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftImmOperatorAssignments.RemoveRange(db.ShiftImmOperatorAssignments
                .Where(a => a.ShiftPlanItemId == fixture.Item.Id));
            foreach (var task in await db.ShiftTasks.Where(t => fixture.Tasks.Select(x => x.Id).Contains(t.Id)).ToListAsync())
            {
                task.PackagingUnit = null;
                task.PartWeightGrams = 99;
            }
            await db.SaveChangesAsync();
        }

        var response = await PatchAsync(manager, fixture.ClosureId, "Атомарная пакетная правка",
            Manual(first.Id, 6, 70, 25, 2, 9, 2),
            Manual(second.Id, null, 60, 20, 1.25m, 4, 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        body.GetProperty("errors").Deserialize<List<FieldError>>(JsonOptions)!.Should().Contain(
            new FieldError(second.Id, "actualPartWeightGrams", "required"));
        (await ManualSnapshotAsync(first.Id)).Should().BeEquivalentTo(beforeFirst);
        (await ManualSnapshotAsync(second.Id)).Should().BeEquivalentTo(beforeSecond);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftClosureCorrections.CountAsync(c => c.ShiftClosureId == fixture.ClosureId))
                .Should().Be(0);
        }

        var valid = await PatchAsync(manager, fixture.ClosureId, "Коррекция по снимку",
            Manual(first.Id, 6, 70, 25, 2, 9, 2));
        valid.StatusCode.Should().Be(HttpStatusCode.NoContent, await valid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correction_RejectsBlankReasonNoChangesAndMissingClosureOrTask()
    {
        var fixture = await SeedClosedAsync();
        var task = fixture.Tasks.Single();
        var manager = await ClientAsync("test_manager", "Manager123!");

        (await PatchAsync(manager, fixture.ClosureId, "   ",
            Manual(task.Id, manualGoodQuantity: 7))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noChanges = await PatchAsync(manager, fixture.ClosureId, "Без изменений", Manual(task.Id));
        noChanges.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(noChanges)).GetProperty("message").GetString().Should().Be("noChanges");
        (await PatchAsync(manager, fixture.ClosureId, "Без изменений", Array.Empty<object>()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatchAsync(manager, Guid.NewGuid(), "Нет рапорта",
            Manual(task.Id, manualGoodQuantity: 7))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PatchAsync(manager, fixture.ClosureId, "Нет задания",
            Manual(Guid.NewGuid(), manualGoodQuantity: 7))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Correction_AllowsIndependentOutputEditsAndOverridesForcedZeroForUnstaffedClosedRow()
    {
        var fixture = await SeedClosedAsync(staffed: false, fallback: false);
        var task = fixture.Tasks.Single();
        var manager = await ClientAsync("test_manager", "Manager123!");
        var closed = await RequiredJsonAsync(await manager.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"));
        var entry = Entry(closed, task.Id);
        entry.GetProperty("hasOperators").GetBoolean().Should().BeFalse();
        entry.GetProperty("manualGoodQuantity").GetInt32().Should().Be(0);
        entry.GetProperty("manualDefectQuantity").GetInt32().Should().Be(0);

        var goodOnly = await PatchAsync(manager, fixture.ClosureId, "Уточнены только годные",
            Manual(task.Id, manualGoodQuantity: 7, manualDefectQuantity: 0));
        goodOnly.StatusCode.Should().Be(HttpStatusCode.NoContent, await goodOnly.Content.ReadAsStringAsync());
        var afterGood = Entry(await RequiredJsonAsync(await manager.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}")), task.Id);
        afterGood.GetProperty("manualGoodQuantity").GetInt32().Should().Be(7);
        afterGood.GetProperty("manualDefectQuantity").GetInt32().Should().Be(0);

        var defectOnly = await PatchAsync(manager, fixture.ClosureId, "Уточнён только брак",
            Manual(task.Id, manualGoodQuantity: 7, manualDefectQuantity: 3));
        defectOnly.StatusCode.Should().Be(HttpStatusCode.NoContent, await defectOnly.Content.ReadAsStringAsync());
        var afterDefect = Entry(await RequiredJsonAsync(await manager.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}")), task.Id);
        afterDefect.GetProperty("manualGoodQuantity").GetInt32().Should().Be(7);
        afterDefect.GetProperty("manualDefectQuantity").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Correction_RejectsDuplicateTaskInputsIncludingNetNoOpAndDifferentValues()
    {
        var fixture = await SeedClosedAsync();
        var task = fixture.Tasks.Single();
        var manager = await ClientAsync("test_manager", "Manager123!");

        var netNoOp = await PatchAsync(manager, fixture.ClosureId, "Дублирующаяся строка",
            Manual(task.Id, manualGoodQuantity: 5),
            Manual(task.Id, manualGoodQuantity: 4));

        netNoOp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(netNoOp)).GetProperty("message").GetString().Should().Be("duplicateTask");
        (await ManualSnapshotAsync(task.Id)).ManualGoodQuantity.Should().Be(4);

        var differentValues = await PatchAsync(manager, fixture.ClosureId, "Дублирующаяся строка",
            Manual(task.Id, manualGoodQuantity: 5),
            Manual(task.Id, manualGoodQuantity: 6));

        differentValues.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(differentValues)).GetProperty("message").GetString().Should().Be("duplicateTask");
        (await ManualSnapshotAsync(task.Id)).ManualGoodQuantity.Should().Be(4);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosureCorrections.CountAsync(c => c.ShiftClosureId == fixture.ClosureId))
            .Should().Be(0);
    }

    private async Task<ClosedFixture> SeedClosedAsync(
        int taskCount = 1, bool staffed = true, bool fallback = true)
    {
        var now = PastAnchor();
        var start = Utc(now.AddHours(-4));
        var end = Utc(now.AddHours(-2));
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var tasks = new List<ShiftTask>();
        for (var index = 0; index < taskCount; index++)
            tasks.Add(await SeedTaskAsync(immId, start, end));
        if (staffed)
            await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, start);
        await SeedStatusAsync(immId, fallback ? ImmStatus.Offline : ImmStatus.Auto, start, end);
        var leadClient = await ClientAsync(lead);
        var draft = await leadClient.PutAsJsonAsync($"/api/shift-closures/items/{item.Id}/draft", new
        {
            entries = tasks.Select(t => staffed
                ? Manual(t.Id)
                : Manual(t.Id, manualGoodQuantity: null, manualDefectQuantity: null)).ToArray()
        });
        draft.StatusCode.Should().Be(HttpStatusCode.OK, await draft.Content.ReadAsStringAsync());
        var closed = await RequiredJsonAsync(await leadClient.PostAsJsonAsync(
            $"/api/shift-closures/items/{item.Id}/close", new { confirmEarlyClose = false }));
        return new ClosedFixture(lead, item, closed.GetProperty("id").GetGuid(), tasks);
    }

    private static object Manual(
        Guid taskId,
        decimal? actualPartWeightGrams = 5,
        int? manualFullCycleSeconds = 60,
        int? actualPackagingUnit = 20,
        decimal? packagingPlaces = 1.25m,
        int? manualGoodQuantity = 4,
        int? manualDefectQuantity = 1) => new
    {
        shiftTaskId = taskId,
        actualPartWeightGrams,
        manualFullCycleSeconds,
        actualPackagingUnit,
        packagingPlaces,
        manualGoodQuantity,
        manualDefectQuantity
    };

    private static Task<HttpResponseMessage> PatchAsync(
        HttpClient client, Guid closureId, string reason, params object[] entries) =>
        client.PatchAsJsonAsync($"/api/shift-closures/{closureId}/corrections", new { reason, entries });

    private async Task<HttpClient> ClientAsync(TestUser user) =>
        await ClientAsync(user.Login, user.Password);

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"correction_{role}_{suffix}";
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
            Name = $"CORRECTION-{Guid.NewGuid():N}",
            InventoryNumber = $"INV-{Guid.NewGuid():N}"[..16],
            TemplateId = _factory.TestTemplateId,
            IsActive = true
        };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private static ShiftPlanItem Item(string leadId, DateTime start, DateTime end) => new()
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
        var local = schedule.ToLocal(start);
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

    private async Task SeedAssignmentAsync(
        Guid itemId, Guid immId, string operatorId, DateTime assignedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = itemId,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = Utc(assignedAt),
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

    private sealed record ManualSnapshot(
        decimal? ActualPartWeightGrams,
        int? ManualFullCycleSeconds,
        int? ActualPackagingUnit,
        decimal? PackagingPlaces,
        int? ManualGoodQuantity,
        int? ManualDefectQuantity);

    private sealed record TaskSnapshot(
        DateTime? UpdatedAt,
        int ActualQuantity,
        int DefectQuantity,
        decimal ActualMaterialWeightGrams,
        TaskStatus Status);

    private async Task<ManualSnapshot> ManualSnapshotAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var entry = await db.ShiftClosureEntries.AsNoTracking().SingleAsync(e => e.ShiftTaskId == taskId);
        return new ManualSnapshot(entry.ActualPartWeightGrams, entry.ManualFullCycleSeconds,
            entry.ActualPackagingUnit, entry.PackagingPlaces,
            entry.ManualGoodQuantity, entry.ManualDefectQuantity);
    }

    private async Task<TaskSnapshot> TaskSnapshotAsync(Guid taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = await db.ShiftTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        return new TaskSnapshot(task.UpdatedAt, task.ActualQuantity, task.DefectQuantity,
            task.ActualMaterialWeightGrams, task.Status);
    }

    private async Task<string> UserIdAsync(string login)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Users.Where(u => u.UserName == login).Select(u => u.Id).SingleAsync();
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

    private static DateTime PastAnchor()
    {
        var value = DateTime.UtcNow.AddDays(-100 - 2 * Interlocked.Increment(ref _windowOffset));
        return new DateTime(value.Year, value.Month, value.Day,
            value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
