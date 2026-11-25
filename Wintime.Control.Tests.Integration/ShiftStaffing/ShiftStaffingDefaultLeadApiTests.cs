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
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ShiftStaffing;

[Collection("Integration")]
public class ShiftStaffingDefaultLeadApiTests : IClassFixture<IntegrationTestFactory>
{
    private const long ScheduleLockKey = 0x5754_5343_4845_4455;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 7000;
    private static int _positionOffset = 20000;

    private readonly IntegrationTestFactory _factory;

    public ShiftStaffingDefaultLeadApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password, string FullName);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nearest_VirtualDefault_MaterializesSelectedShiftAndReturnsStableItemId(bool current)
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var isolationLead = await SeedUserAsync(UserRole.ShiftLead);
        const int position = 1;
        var previousDefault = await SetDefaultAsync(position, lead.Id);
        var isolatedItems = await IsolateSavedFallbackItemsAsync(position, isolationLead.Id);
        DateOnly? materializedDate = null;
        var client = await ClientAsync(lead);
        var admin = await AdminClientAsync();
        try
        {
            await WithScheduleAsync(admin, SingleShiftAroundNow(current), async () =>
            {
                var first = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/nearest"));
                var item = first.GetProperty("item");
                item.ValueKind.Should().NotBe(JsonValueKind.Null);
                item.GetProperty("state").GetString().Should().Be(current ? "Current" : "Future");
                item.GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
                var itemId = item.GetProperty("itemId").GetGuid();
                itemId.Should().NotBe(Guid.Empty);
                materializedDate = DateOnly.Parse(item.GetProperty("shiftDate").GetString()!);

                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                    var saved = await db.ShiftPlanItems.AsNoTracking()
                        .Include(i => i.Day)
                        .SingleAsync(i => i.Id == itemId);
                    saved.ShiftLeadId.Should().BeNull("материализация не создаёт явный override");
                    saved.Day.ShiftDate.Should().Be(materializedDate);
                    (await db.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == materializedDate))
                        .Should().Be(1);
                    (await db.ShiftImmOperatorAssignments.CountAsync(a => a.ShiftPlanItemId == itemId))
                        .Should().Be(0);
                    (await db.ShiftClosures.CountAsync(c => c.ShiftPlanItemId == itemId)).Should().Be(0);
                }

                var second = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/nearest"));
                second.GetProperty("item").GetProperty("itemId").GetGuid().Should().Be(itemId);
                using var verifyScope = _factory.Services.CreateScope();
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await verifyDb.ShiftPlanDays.CountAsync(d => d.ShiftDate == materializedDate)).Should().Be(1);
            });
        }
        finally
        {
            if (materializedDate.HasValue) await DeleteDayAsync(materializedDate.Value);
            await SetDefaultAsync(position, previousDefault);
            await RestoreSavedFallbackItemsAsync(isolatedItems);
        }
    }

    [Fact]
    public async Task Nearest_DefaultChangedWhileWaitingForScheduleLock_Returns409()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var replacement = await SeedUserAsync(UserRole.ShiftLead);
        var isolationLead = await SeedUserAsync(UserRole.ShiftLead);
        const int position = 1;
        var previousDefault = await SetDefaultAsync(position, lead.Id);
        var isolatedItems = await IsolateSavedFallbackItemsAsync(position, isolationLead.Id);
        var existingDayIds = await DayIdsAsync();
        var client = await ClientAsync(lead);
        var admin = await AdminClientAsync();
        try
        {
            await WithScheduleAsync(admin, SingleShiftAroundNow(current: true), async () =>
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
                                 $"SELECT pg_advisory_xact_lock({ScheduleLockKey})", connection, transaction))
                    await lockCommand.ExecuteNonQueryAsync();

                var request = client.GetAsync("/api/shift-staffing/nearest");
                await WaitForAdvisoryWaiterAsync(connection, transaction);
                await SetDefaultAsync(position, replacement.Id);
                await transaction.CommitAsync();

                var response = await request;
                response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                    await response.Content.ReadAsStringAsync());
            });
        }
        finally
        {
            await DeleteNewDaysAsync(existingDayIds);
            await SetDefaultAsync(position, previousDefault);
            await RestoreSavedFallbackItemsAsync(isolatedItems);
        }
    }

    [Fact]
    public async Task ManagerFallback_DoesNotGiveShiftLeadAccess_AndEnablesManagerNearest()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var now = DateTime.UtcNow;
        var item = (await SeedDayAsync(Item(
            NextPosition(), null, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var manager = await ManagerClientAsync();
        var fallbackManager = await FirstActiveManagerAsync();
        var leadClient = await ClientAsync(lead);
        try
        {
            var nearest = await RequiredJsonAsync(await leadClient.GetAsync("/api/shift-staffing/nearest"));
            nearest.GetProperty("item").ValueKind.Should().Be(JsonValueKind.Null);
            (await leadClient.GetAsync($"/api/shift-staffing/items/{item.Id}")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);

            var body = await RequiredJsonAsync(await manager.GetAsync($"/api/shift-staffing/items/{item.Id}"));
            body.GetProperty("shiftLeadId").GetString().Should().Be(fallbackManager.Id);
            body.GetProperty("shiftLeadName").GetString().Should().Be(fallbackManager.FullName);
            body.GetProperty("shift").GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
        }
        finally
        {
            await DeleteDayAsync(item.Day.ShiftDate);
        }
    }

    [Fact]
    public async Task History_UsesEffectiveDefaultLead_WithoutMaterializingPastDays()
    {
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var now = DateTime.UtcNow;
        var position = NextPosition();
        var item = (await SeedDayAsync(Item(
            position, null, now.AddHours(-5), now.AddHours(-4)))).Items.Single();
        var previousDefault = await SetDefaultAsync(position, lead.Id);
        var client = await ClientAsync(lead);
        try
        {
            int dayCountBefore;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                dayCountBefore = await db.ShiftPlanDays.CountAsync();
            }

            var body = await RequiredJsonAsync(await client.GetAsync("/api/shift-staffing/history"));
            var historyItem = body.EnumerateArray().Should().ContainSingle(
                i => i.GetProperty("itemId").GetGuid() == item.Id).Subject;
            historyItem.GetProperty("canEditOperators").GetBoolean().Should().BeTrue();

            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await verifyDb.ShiftPlanDays.CountAsync()).Should().Be(dayCountBefore);
        }
        finally
        {
            await DeleteDayAsync(item.Day.ShiftDate);
            await SetDefaultAsync(position, previousDefault);
        }
    }

    [Fact]
    public async Task ReplacingDefault_TransfersAccessAndPreservesAssignments_StaleLeadGets403()
    {
        var oldLead = await SeedUserAsync(UserRole.ShiftLead);
        var newLead = await SeedUserAsync(UserRole.ShiftLead);
        var firstOperator = await SeedUserAsync(UserRole.Operator);
        var secondOperator = await SeedUserAsync(UserRole.Operator);
        var immId = await SeedImmAsync();
        var now = DateTime.UtcNow;
        var position = NextPosition();
        var item = (await SeedDayAsync(Item(
            position, null, now.AddHours(-1), now.AddHours(1)))).Items.Single();
        var original = await SeedAssignmentAsync(item, immId, firstOperator.Id, oldLead.Id);
        var previousDefault = await SetDefaultAsync(position, oldLead.Id);
        await SetDefaultAsync(position, newLead.Id);
        var oldClient = await ClientAsync(oldLead);
        var newClient = await ClientAsync(newLead);
        try
        {
            (await AssignAsync(oldClient, item.Id, immId, secondOperator.Id)).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
            (await oldClient.GetAsync($"/api/shift-staffing/items/{item.Id}")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);

            var assigned = await RequiredJsonAsync(
                await AssignAsync(newClient, item.Id, immId, secondOperator.Id));
            assigned.GetProperty("operatorId").GetString().Should().Be(secondOperator.Id);
            var body = await RequiredJsonAsync(await newClient.GetAsync($"/api/shift-staffing/items/{item.Id}"));
            body.GetProperty("shiftLeadId").GetString().Should().Be(newLead.Id);
            body.GetProperty("shiftLeadName").GetString().Should().Be(newLead.FullName);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var assignments = await db.ShiftImmOperatorAssignments.AsNoTracking()
                .Where(a => a.ShiftPlanItemId == item.Id).ToListAsync();
            assignments.Should().HaveCount(2);
            assignments.Should().ContainSingle(a => a.Id == original.Id)
                .Which.Should().Match<ShiftImmOperatorAssignment>(a =>
                    a.OperatorId == firstOperator.Id && a.UnassignedAt == null && a.CreatedById == oldLead.Id);
        }
        finally
        {
            await DeleteDayAsync(item.Day.ShiftDate);
            await SetDefaultAsync(position, previousDefault);
        }
    }

    private static List<ShiftScheduleItemDto> SingleShiftAroundNow(bool current)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var start = localNow.TimeOfDay.Add(current ? TimeSpan.FromHours(-1) : TimeSpan.FromHours(1));
        var startMinutes = ((int)start.TotalMinutes % 1440 + 1440) % 1440;
        return
        [
            new ShiftScheduleItemDto
            {
                StartMinutes = startMinutes,
                DurationMinutes = current ? 120 : 60,
                TimeZoneId = "Europe/Moscow"
            }
        ];
    }

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

    private static Task<HttpResponseMessage> SaveScheduleAsync(
        HttpClient client, List<ShiftScheduleItemDto> shifts) =>
        client.PutAsJsonAsync("/api/shifts", new SaveShiftsRequestDto { Shifts = shifts });

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
        var client = _factory.CreateClient();
        return AuthenticateAsync(client, login, password);
    }

    private static async Task<HttpClient> AuthenticateAsync(
        HttpClient client, string login, string password)
    {
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
        var login = $"staffing_default_{role}_{suffix}";
        const string password = "Passw0rd!";
        var fullName = $"{role} {suffix}";
        var user = new User
        {
            UserName = login,
            NormalizedUserName = login.ToUpperInvariant(),
            Email = $"{login}@test.local",
            NormalizedEmail = $"{login.ToUpperInvariant()}@TEST.LOCAL",
            FullName = fullName,
            EmployeeId = $"EMP-{suffix}"[..12],
            Role = role,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password, fullName);
    }

    private async Task<(string Id, string FullName)> FirstActiveManagerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var manager = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.FullName })
            .FirstAsync();
        return (manager.Id, manager.FullName);
    }

    private async Task<Guid> SeedImmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Wintime.Control.Core.Entities.Imm
        {
            Name = $"STAFFING-DEFAULT-{Guid.NewGuid():N}",
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
        ShiftPlanItem item, Guid immId, string operatorId, string createdById)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var assignment = new ShiftImmOperatorAssignment
        {
            ShiftPlanItemId = item.Id,
            ImmId = immId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = item.StartAtUtc,
            CreatedById = createdById
        };
        db.ShiftImmOperatorAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private static Task<HttpResponseMessage> AssignAsync(
        HttpClient client, Guid itemId, Guid immId, string operatorId) =>
        client.PostAsJsonAsync($"/api/shift-staffing/items/{itemId}/operators",
            new { ImmId = immId, OperatorId = operatorId });

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

    private async Task<List<Guid>> IsolateSavedFallbackItemsAsync(int position, string explicitLeadId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var items = await db.ShiftPlanItems
            .Where(i => i.Position == position && i.ShiftLeadId == null)
            .ToListAsync();
        foreach (var item in items) item.ShiftLeadId = explicitLeadId;
        await db.SaveChangesAsync();
        return items.Select(i => i.Id).ToList();
    }

    private async Task RestoreSavedFallbackItemsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var items = await db.ShiftPlanItems.Where(i => itemIds.Contains(i.Id)).ToListAsync();
        foreach (var item in items) item.ShiftLeadId = null;
        await db.SaveChangesAsync();
    }

    private async Task<HashSet<Guid>> DayIdsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return (await db.ShiftPlanDays.AsNoTracking().Select(d => d.Id).ToListAsync()).ToHashSet();
    }

    private async Task DeleteNewDaysAsync(IReadOnlySet<Guid> existingDayIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var newDays = await db.ShiftPlanDays
            .Where(d => !existingDayIds.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync();
        if (newDays.Count > 0)
            await db.ShiftPlanDays.Where(d => newDays.Contains(d.Id)).ExecuteDeleteAsync();
    }

    private static async Task WaitForAdvisoryWaiterAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var waiting = 0;
        for (var attempt = 0; attempt < 200 && waiting == 0; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
                connection, transaction);
            waiting = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waiting == 0) await Task.Delay(25);
        }
        waiting.Should().BeGreaterThan(0, "nearest должен ждать ShiftScheduleLock перед материализацией");
    }

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
    }

    private static int NextPosition() => Interlocked.Increment(ref _positionOffset);

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))!;
    }
}
