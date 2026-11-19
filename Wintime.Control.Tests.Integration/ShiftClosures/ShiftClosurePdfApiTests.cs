using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
public class ShiftClosurePdfApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 15000;
    private static int _windowOffset;
    private readonly IntegrationTestFactory _factory;

    public ShiftClosurePdfApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(
        string Id, string Login, string Password, string FullName, string? EmployeeId);

    private sealed record DraftFixture(
        TestUser Lead, TestUser Operator, ShiftPlanItem Item, ShiftTask Task);

    private sealed record ClosedFixture(
        TestUser Lead, TestUser Operator, ShiftPlanItem Item, ShiftTask Task, Guid ClosureId);

    [Fact]
    public async Task Blank_ReturnsPdfBeforeDuringAndAfterEndWithoutCreatingClosure()
    {
        var now = DateTime.UtcNow;
        var before = await SeedDraftAsync(now.AddHours(2), now.AddHours(4), TaskStatus.Planned);
        var during = await SeedDraftAsync(now.AddHours(-1), now.AddHours(2), TaskStatus.InProgress);
        var after = await SeedDraftAsync(now.AddHours(-4), now.AddHours(-2), TaskStatus.Completed);

        foreach (var fixture in new[] { before, during, after })
        {
            var client = await ClientAsync(fixture.Lead);
            await AssertPdfAsync(await client.GetAsync(
                $"/api/shift-closures/items/{fixture.Item.Id}/blank-pdf"));
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftClosures.AnyAsync(c => c.ShiftPlanItemId == fixture.Item.Id)).Should().BeFalse(
                "печать актуального бланка не должна материализовать черновик");
        }
    }

    [Fact]
    public async Task Blank_IsRegeneratedFromCurrentDataAndDoesNotPersistAutomaticSnapshot()
    {
        var now = PastAnchor();
        var fixture = await SeedDraftAsync(now.AddHours(-4), now.AddHours(-2), TaskStatus.Completed);
        var client = await ClientAsync(fixture.Lead);
        var draft = await RequiredJsonAsync(await PutDraftAsync(client, fixture.Item.Id,
            Manual(fixture.Task.Id, 4.5m, 55, 24, 1.25m, 8, 2)));
        var closureId = draft.GetProperty("id").GetGuid();
        var first = await AssertPdfAsync(await client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}/blank-pdf"));

        await SeedCycleAsync(fixture.Task, fixture.Item.StartAtUtc.AddMinutes(50), cavities: 5, 20);
        var current = await RequiredJsonAsync(await client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"));
        Entry(current, fixture.Task.Id).GetProperty("automaticOutputQuantity").GetInt32().Should().Be(7);
        var second = await AssertPdfAsync(await client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}/blank-pdf"));

        second.Should().NotEqual(first, "бланк формируется заново из актуальных данных и серверного времени");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var closure = await db.ShiftClosures.AsNoTracking().SingleAsync(c => c.Id == closureId);
        closure.State.Should().Be(ShiftClosureState.Draft);
        var entry = await db.ShiftClosureEntries.AsNoTracking().SingleAsync(e => e.ShiftClosureId == closureId);
        entry.AutomaticOutputQuantity.Should().Be(0);
        entry.ManualGoodQuantity.Should().Be(8, "PDF не очищает сохранённый электронный черновик");
    }

    [Fact]
    public async Task PdfEndpoints_EnforceStatesMissingClosureOwnershipAndRoles()
    {
        var closed = await SeedClosedAsync();
        var leadClient = await ClientAsync(closed.Lead);
        (await leadClient.GetAsync($"/api/shift-closures/items/{closed.Item.Id}/blank-pdf"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertPdfAsync(await leadClient.GetAsync($"/api/shift-closures/{closed.ClosureId}/pdf"));
        (await leadClient.GetAsync($"/api/shift-closures/{Guid.NewGuid()}/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var now = PastAnchor();
        var draft = await SeedDraftAsync(now.AddHours(-4), now.AddHours(-2), TaskStatus.Completed);
        var draftClient = await ClientAsync(draft.Lead);
        var draftBody = await RequiredJsonAsync(await PutDraftAsync(draftClient, draft.Item.Id,
            Manual(draft.Task.Id, 5, 60, 20, 1, 4, 1)));
        var draftId = draftBody.GetProperty("id").GetGuid();
        (await draftClient.GetAsync($"/api/shift-closures/{draftId}/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignLead = await SeedUserAsync(UserRole.ShiftLead);
        var foreignClient = await ClientAsync(foreignLead);
        (await foreignClient.GetAsync($"/api/shift-closures/items/{closed.Item.Id}/blank-pdf"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreignClient.GetAsync($"/api/shift-closures/{closed.ClosureId}/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var role in new[] { UserRole.Operator, UserRole.Adjuster, UserRole.Observer })
        {
            var client = await ClientAsync(await SeedUserAsync(role));
            (await client.GetAsync($"/api/shift-closures/items/{closed.Item.Id}/blank-pdf"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
            (await client.GetAsync($"/api/shift-closures/{closed.ClosureId}/pdf"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }

        await AssertPdfAsync(await (await ManagerClientAsync()).GetAsync(
            $"/api/shift-closures/{closed.ClosureId}/pdf"));
        await AssertPdfAsync(await (await AdminClientAsync()).GetAsync(
            $"/api/shift-closures/items/{draft.Item.Id}/blank-pdf"));
    }

    [Fact]
    public async Task ClosedPdf_RemainsFrozenWhileTimelineUsesCurrentOperatorAndCorrectionsChangeManual()
    {
        var fixture = await SeedClosedAsync();
        var client = await ClientAsync(fixture.Lead);
        var originalDto = await (await RequiredResponseAsync(client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"))).Content.ReadAsStringAsync();
        var originalPdf = await AssertPdfAsync(await client.GetAsync(
            $"/api/shift-closures/{fixture.ClosureId}/pdf"));

        await MutateCurrentSourcesAsync(fixture, "ПОСЛЕ ЗАКРЫТИЯ");

        var frozenDto = await (await RequiredResponseAsync(client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"))).Content.ReadAsStringAsync();
        using var frozenDocument = JsonDocument.Parse(frozenDto);
        using var originalDocument = JsonDocument.Parse(originalDto);
        var original = originalDocument.RootElement;
        Entry(frozenDocument.RootElement, fixture.Task.Id).GetRawText().Should().Be(
            Entry(original, fixture.Task.Id).GetRawText());
        AssertCurrentTimelineIdentity(frozenDocument.RootElement, fixture, "ПОСЛЕ ЗАКРЫТИЯ");
        frozenDto.Should().NotBe(originalDto,
            "timeline намеренно показывает текущую идентичность по OperatorId");
        PdfContent(await AssertPdfAsync(await client.GetAsync($"/api/shift-closures/{fixture.ClosureId}/pdf")))
            .Should().Be(PdfContent(originalPdf),
                "содержимое закрытого PDF строится только из замороженных Entries/Shares");

        var manager = await ManagerClientAsync();
        var correction = await manager.PatchAsJsonAsync(
            $"/api/shift-closures/{fixture.ClosureId}/corrections", new
            {
                reason = "Исправление перед повторной печатью",
                entries = new[] { Manual(fixture.Task.Id, 5.5m, 60, 20, 1.25m, 4, 1) }
            });
        correction.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await correction.Content.ReadAsStringAsync());
        var correctedDtoText = await (await RequiredResponseAsync(client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"))).Content.ReadAsStringAsync();
        var correctedDto = JsonDocument.Parse(correctedDtoText).RootElement;
        var correctedEntry = Entry(correctedDto, fixture.Task.Id);
        correctedEntry.GetProperty("actualPartWeightGrams").GetDecimal().Should().Be(5.5m);
        correctedEntry.GetProperty("shiftPlanQuantity").GetRawText().Should()
            .Be(Entry(original, fixture.Task.Id).GetProperty("shiftPlanQuantity").GetRawText());
        correctedEntry.GetProperty("automaticOutputQuantity").GetRawText().Should()
            .Be(Entry(original, fixture.Task.Id).GetProperty("automaticOutputQuantity").GetRawText());
        correctedEntry.GetProperty("outputShares").GetRawText().Should()
            .Be(Entry(original, fixture.Task.Id).GetProperty("outputShares").GetRawText());
        var correctedPdf = await AssertPdfAsync(await client.GetAsync(
            $"/api/shift-closures/{fixture.ClosureId}/pdf"));
        PdfContent(correctedPdf).Should().NotBe(PdfContent(originalPdf));

        await MutateCurrentSourcesAsync(fixture, "ПОСЛЕ ИСПРАВЛЕНИЯ");
        var reread = await (await RequiredResponseAsync(client.GetAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}"))).Content.ReadAsStringAsync();
        using var rereadDocument = JsonDocument.Parse(reread);
        Entry(rereadDocument.RootElement, fixture.Task.Id).GetRawText().Should().Be(
            Entry(correctedDto, fixture.Task.Id).GetRawText());
        AssertCurrentTimelineIdentity(rereadDocument.RootElement, fixture, "ПОСЛЕ ИСПРАВЛЕНИЯ");
        PdfContent(await AssertPdfAsync(await client.GetAsync($"/api/shift-closures/{fixture.ClosureId}/pdf")))
            .Should().Be(PdfContent(correctedPdf));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftClosureCorrections.CountAsync(c => c.ShiftClosureId == fixture.ClosureId))
            .Should().Be(1);
    }

    private async Task<DraftFixture> SeedDraftAsync(
        DateTime start, DateTime end, TaskStatus status)
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var operatorUser = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var item = (await SeedDayAsync(Item(lead.Id, start, end))).Items.Single();
        var task = await SeedTaskAsync(immId, start, end, status);
        await SeedAssignmentAsync(item.Id, immId, operatorUser.Id, start, status == TaskStatus.Planned ? end : null);
        if (status != TaskStatus.Planned)
            await SeedCycleAsync(task, start.AddMinutes(30), cavities: 2, durationSeconds: 40);
        await SeedStatusAsync(immId, ImmStatus.Auto, start, end);
        return new DraftFixture(lead, operatorUser, item, task);
    }

    private async Task<ClosedFixture> SeedClosedAsync()
    {
        var now = PastAnchor();
        var fixture = await SeedDraftAsync(now.AddHours(-4), now.AddHours(-2), TaskStatus.Completed);
        await SeedRecipeAsync(fixture.Task.Id);
        var client = await ClientAsync(fixture.Lead);
        await RequiredJsonAsync(await PutDraftAsync(client, fixture.Item.Id,
            Manual(fixture.Task.Id, 5, 60, 20, 1.25m, 4, 1)));
        var closed = await RequiredJsonAsync(await client.PostAsJsonAsync(
            $"/api/shift-closures/items/{fixture.Item.Id}/close", new { confirmEarlyClose = false }));
        return new ClosedFixture(fixture.Lead, fixture.Operator, fixture.Item, fixture.Task,
            closed.GetProperty("id").GetGuid());
    }

    private async Task MutateCurrentSourcesAsync(ClosedFixture fixture, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = await db.ShiftTasks.Include(t => t.Imm).Include(t => t.Product)
            .Include(t => t.RecipeIngredients).ThenInclude(i => i.Material)
            .SingleAsync(t => t.Id == fixture.Task.Id);
        task.Imm.Name = $"ТЕКУЩИЙ ТПА {suffix}";
        task.Product!.Name = $"ТЕКУЩЕЕ ИЗДЕЛИЕ {suffix}";
        task.Product.RunnerWeightGrams = 99;
        task.PartWeightGrams = 99;
        task.PackagingUnit = 99;
        task.Cavities = 9;
        foreach (var ingredient in task.RecipeIngredients)
        {
            ingredient.Percent = 77;
            ingredient.Material!.Name = $"ТЕКУЩИЙ МАТЕРИАЛ {suffix}";
        }
        var operatorUser = await db.Users.SingleAsync(u => u.Id == fixture.Operator.Id);
        operatorUser.FullName = $"ТЕКУЩИЙ ЛИТЕЙЩИК {suffix}";
        operatorUser.EmployeeId = $"CURRENT-{suffix}";
        foreach (var assignment in db.ShiftImmOperatorAssignments
            .Where(a => a.ShiftPlanItemId == fixture.Item.Id))
            assignment.UnassignedAt = Utc(fixture.Item.EndAtUtc);
        var end = fixture.Item.EndAtUtc.AddMinutes(-1);
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = task.ImmId,
            TaskId = task.Id,
            MoldId = task.MoldId,
            StartTime = Utc(end.AddSeconds(-60)),
            EndTime = Utc(end),
            DurationSeconds = 60,
            Cavities = 100,
            IsSuccessful = true
        });
        await db.SaveChangesAsync();
    }

    private static void AssertCurrentTimelineIdentity(
        JsonElement dto, ClosedFixture fixture, string suffix)
    {
        var timelineOperator = dto.GetProperty("timelines").EnumerateArray().Single()
            .GetProperty("intervals").EnumerateArray().Single()
            .GetProperty("operators").EnumerateArray().Single();
        timelineOperator.GetProperty("operatorId").GetString().Should().Be(fixture.Operator.Id);
        timelineOperator.GetProperty("fullName").GetString().Should()
            .Be($"ТЕКУЩИЙ ЛИТЕЙЩИК {suffix}");
        timelineOperator.GetProperty("employeeId").GetString().Should().Be($"CURRENT-{suffix}");
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

    private static string PdfContent(byte[] bytes) => Regex.Replace(
        Encoding.Latin1.GetString(bytes),
        @"/(CreationDate|ModDate) \(D:[^)]*\)",
        "/$1 (D:volatile)");

    private static async Task<byte[]> AssertPdfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        fileName.Should().NotBeNull();
        fileName!.Trim('"').Should().EndWith(".pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        return bytes;
    }

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

    private Task<HttpClient> ManagerClientAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdminClientAsync() => ClientAsync("admin", "Admin123!");

    private async Task<TestUser> SeedUserAsync(UserRole role)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var login = $"pdf_{role}_{suffix}";
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
            Name = $"PDF-{Guid.NewGuid():N}",
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

    private async Task<ShiftTask> SeedTaskAsync(
        Guid immId, DateTime start, DateTime end, TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        var planned = Utc(start);
        var local = schedule.ToLocal(planned);
        var task = new ShiftTask
        {
            ImmId = immId,
            MoldId = _factory.TestMoldId,
            ProductId = _factory.TestProductId,
            PlanQuantity = 10,
            Cavities = 2,
            PartWeightGrams = 5,
            PackagingUnit = 20,
            Status = status,
            PlannedDate = DateOnly.FromDateTime(local),
            PlannedSetupStartTime = TimeOnly.FromDateTime(local),
            PlannedSetupMinutes = 15,
            PlannedFullCycleSeconds = 60,
            SetupStartedAt = status == TaskStatus.Planned ? null : Utc(start),
            StartedAt = status == TaskStatus.Planned ? null : Utc(start),
            CompletedAt = status == TaskStatus.Completed ? Utc(end) : null
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

    private async Task SeedAssignmentAsync(
        Guid itemId, Guid immId, string operatorId, DateTime assignedAt, DateTime? unassignedAt)
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
            Name = $"PDF PLASTIC {suffix}",
            IsActive = true
        };
        var colorant = new Material
        {
            Type = MaterialType.Colorant,
            Article = $"CO-{suffix}",
            Name = $"PDF COLORANT {suffix}",
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

    private static async Task<HttpResponseMessage> RequiredResponseAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response;
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }

    private static JsonElement Entry(JsonElement body, Guid taskId) => body.GetProperty("entries")
        .EnumerateArray().Should().ContainSingle(e => e.GetProperty("taskId").GetGuid() == taskId).Subject;

    private static DateTime PastAnchor()
    {
        var value = DateTime.UtcNow.AddDays(-300 - 2 * Interlocked.Increment(ref _windowOffset));
        return new DateTime(value.Year, value.Month, value.Day,
            value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
}
