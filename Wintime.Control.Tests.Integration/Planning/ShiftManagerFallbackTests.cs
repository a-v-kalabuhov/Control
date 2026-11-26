using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Planning;

[Collection("Integration")]
public class ShiftManagerFallbackTests : IClassFixture<IntegrationTestFactory>
{
    private const long ScheduleLockKey = 0x5754_5343_4845_4455;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 12000;
    private static int _windowOffset;
    private static int _positionOffset = 30000;

    private readonly IntegrationTestFactory _factory;

    public ShiftManagerFallbackTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password);
    private sealed record ManagerState(string Id, UserRole Role, bool IsActive);
    private sealed record RaceResult(
        HttpResponseMessage Update,
        HttpResponseMessage? Operation,
        bool BothWaited);

    [Fact]
    public async Task ArchiveFirstManager_WaitsForScheduleLockAndSelectsNextManager()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);

            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { IsActive = false });

            race.BothWaited.Should().BeTrue("архивация Manager должна ждать ShiftScheduleLock");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);
        }
        finally
        {
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task RestoreEarlierManager_WaitsForScheduleLockAndRestoresFallback()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(
            UserRole.Manager, DateTime.UtcNow.AddYears(-10), isActive: false);
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);

            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { IsActive = true });

            race.BothWaited.Should().BeTrue("восстановление Manager должно ждать ShiftScheduleLock");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);
        }
        finally
        {
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task DemoteFirstManager_WaitsForScheduleLockAndExcludesHimFromFallback()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);

            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { Role = nameof(UserRole.ShiftLead) });

            race.BothWaited.Should().BeTrue("понижение Manager должно ждать ShiftScheduleLock");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);
        }
        finally
        {
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task PromoteEarlierUserToManager_WaitsForScheduleLockAndRestoresFallback()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Operator, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);

            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { Role = nameof(UserRole.Manager) });

            race.BothWaited.Should().BeTrue("назначение роли Manager должно ждать ShiftScheduleLock");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);
        }
        finally
        {
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task NonManagerRoleChange_WaitsForScheduleLockToAvoidStaleRoleDecision()
    {
        var admin = await AdminClientAsync();
        var user = await SeedUserAsync(UserRole.Adjuster, DateTime.UtcNow.AddYears(-10));
        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, user.Id,
                new UpdatePersonnelRequestDto { Role = nameof(UserRole.ShiftLead) });

            race.BothWaited.Should().BeTrue(
                "решение о блокировке нельзя принимать по снимку роли, который может устареть");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
        }
        finally
        {
            await ArchiveUsersAsync(user.Id);
        }
    }

    [Fact]
    public async Task CreateLaterManager_DoesNotChangeCurrentFallback()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        string? laterId = null;
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);
            var login = $"later_manager_{Guid.NewGuid():N}";

            var response = await admin.PostAsJsonAsync("/api/personnel", new CreatePersonnelRequestDto
            {
                FullName = "Более поздний руководитель",
                Login = login,
                Password = "Passw0rd!",
                Role = nameof(UserRole.Manager)
            });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            laterId = (await response.Content.ReadFromJsonAsync<PersonnelDto>())!.Id;
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var later = await db.Users.AsNoTracking().SingleAsync(u => u.Id == laterId);
            later.CreatedAt.Should().BeAfter(DateTime.UtcNow.AddYears(-10));
            (await FallbackManagerIdAsync(admin)).Should().Be(first.Id);
        }
        finally
        {
            await ArchiveUsersAsync(laterId == null ? new[] { first.Id } : new[] { first.Id, laterId });
        }
    }

    [Fact]
    public async Task DemoteLastManager_WaitsForScheduleLockAndLeavesNoFallback()
    {
        var admin = await AdminClientAsync();
        var only = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var previous = await DeactivateOtherManagersAsync(only.Id);
        try
        {
            (await FallbackManagerIdAsync(admin)).Should().Be(only.Id);

            var race = await UpdateUnderScheduleLockAsync(admin, only.Id,
                new UpdatePersonnelRequestDto { Role = nameof(UserRole.Operator) });

            race.BothWaited.Should().BeTrue("удаление последнего Manager из fallback должно ждать ShiftScheduleLock");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            (await FallbackManagerIdAsync(admin)).Should().BeNull();
        }
        finally
        {
            await RestoreManagersAsync(previous);
            await ArchiveUsersAsync(only.Id);
        }
    }

    [Fact]
    public async Task ManagerLoginClear_RevokesRefreshTokensOnlyAfterCommit()
    {
        var admin = await AdminClientAsync();
        var manager = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var refreshToken = $"refresh-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
                .Store(refreshToken, manager.Id, DateTime.UtcNow.AddDays(1));
        }

        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, manager.Id,
                new UpdatePersonnelRequestDto { Login = "" },
                beforeRelease: () =>
                {
                    using var scope = _factory.Services.CreateScope();
                    scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
                        .GetActive(refreshToken).Should().NotBeNull(
                            "до commit очистка логина ещё не должна отзывать refresh-token");
                    return Task.CompletedTask;
                });

            race.BothWaited.Should().BeTrue();
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            using var verifyScope = _factory.Services.CreateScope();
            verifyScope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
                .GetActive(refreshToken).Should().BeNull();
        }
        finally
        {
            await ArchiveUsersAsync(manager.Id);
        }
    }

    [Fact]
    public async Task AdminLoginClear_WaitsForScheduleLockBeforeLastAdminGuard()
    {
        var admin = await AdminClientAsync();
        var otherAdmin = await SeedUserAsync(UserRole.Admin, DateTime.UtcNow.AddYears(-10));
        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, otherAdmin.Id,
                new UpdatePersonnelRequestDto { Login = "" });

            race.BothWaited.Should().BeTrue(
                "проверка последнего администратора должна быть сериализована с другими отзывами доступа");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
        }
        finally
        {
            await ArchiveUsersAsync(otherAdmin.Id);
        }
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("role")]
    public async Task ManagerArchiveOrRoleChange_RevokesShiftAccessWithExistingToken(string mode)
    {
        var admin = await AdminClientAsync();
        var manager = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var client = await ClientAsync(manager.Login, manager.Password);
        try
        {
            (await client.GetAsync("/api/shift-closures/pending")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/shift-staffing/operators")).StatusCode.Should().Be(HttpStatusCode.OK);
            object update = mode == "archive"
                ? new { isActive = false, role = (string?)null }
                : new { isActive = (bool?)null, role = nameof(UserRole.Observer) };

            (await admin.PutAsJsonAsync($"/api/personnel/{manager.Id}", update)).StatusCode.Should()
                .Be(HttpStatusCode.NoContent);

            (await client.GetAsync("/api/shift-closures/pending")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
            (await client.GetAsync("/api/shift-staffing/operators")).StatusCode.Should()
                .Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await ArchiveUsersAsync(manager.Id);
        }
    }

    [Fact]
    public async Task ManagerArchiveRacingMaterialize_SerializesAndReturnsConsistentFallback()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        var date = NewDate();
        var previousDefault = await SetDefaultAsync(1, null);
        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { IsActive = false },
                () => admin.PostAsync(
                    $"/api/shift-plan/days/{date:yyyy-MM-dd}/items/1/materialize", null));

            race.BothWaited.Should().BeTrue("personnel и materialize должны сериализоваться");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            race.Operation!.StatusCode.Should().Be(HttpStatusCode.OK,
                await race.Operation.Content.ReadAsStringAsync());
            var materialized = await JsonAsync(race.Operation);
            var materializedItem = Item(materialized, 1);
            materializedItem.GetProperty("shiftLeadId").GetString().Should().BeOneOf(first.Id, second.Id);
            materializedItem.GetProperty("shiftLeadSource").GetString().Should().Be("ManagerFallback");

            var current = await GetDayAsync(admin, date);
            Item(current, 1).GetProperty("shiftLeadId").GetString().Should().Be(second.Id);
            Item(current, 1).GetProperty("shiftLeadSource").GetString().Should().Be("ManagerFallback");
        }
        finally
        {
            await DeleteDayAsync(date);
            await SetDefaultAsync(1, previousDefault);
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    [Fact]
    public async Task ManagerArchiveRacingStaffing_SerializesAndPreservesAssignment()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        var operatorUser = await SeedUserAsync(UserRole.Operator, DateTime.UtcNow.AddYears(-8));
        var immId = await _factory.CreateFreshImmAsync();
        var (start, end) = PastWindow();
        var item = await SeedItemAsync(start, end);
        var date = item.Day.ShiftDate;
        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { IsActive = false },
                () => admin.PostAsJsonAsync($"/api/shift-staffing/items/{item.Id}/operators", new
                {
                    immId,
                    operatorId = operatorUser.Id,
                    assignedAt = start,
                    unassignedAt = end
                }));

            race.BothWaited.Should().BeTrue("personnel и staffing должны сериализоваться");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            race.Operation!.StatusCode.Should().Be(HttpStatusCode.OK,
                await race.Operation.Content.ReadAsStringAsync());
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftImmOperatorAssignments.CountAsync(a => a.ShiftPlanItemId == item.Id))
                .Should().Be(1);
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);
        }
        finally
        {
            await DeleteDayAsync(date);
            await ArchiveUsersAsync(first.Id, second.Id, operatorUser.Id);
        }
    }

    [Fact]
    public async Task ManagerArchiveRacingClose_SerializesAndKeepsFrozenClosedLead()
    {
        var admin = await AdminClientAsync();
        var first = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-10));
        var second = await SeedUserAsync(UserRole.Manager, DateTime.UtcNow.AddYears(-9));
        var (start, end) = PastWindow();
        var item = await SeedItemAsync(start, end);
        var date = item.Day.ShiftDate;
        try
        {
            var race = await UpdateUnderScheduleLockAsync(admin, first.Id,
                new UpdatePersonnelRequestDto { IsActive = false },
                () => admin.PostAsJsonAsync($"/api/shift-closures/items/{item.Id}/close",
                    new CloseShiftClosureRequest(false)));

            race.BothWaited.Should().BeTrue("personnel и close должны сериализоваться");
            race.Update.StatusCode.Should().Be(HttpStatusCode.NoContent,
                await race.Update.Content.ReadAsStringAsync());
            race.Operation!.StatusCode.Should().Be(HttpStatusCode.OK,
                await race.Operation.Content.ReadAsStringAsync());
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var saved = await db.ShiftPlanItems.AsNoTracking()
                .Include(i => i.Closure)
                .SingleAsync(i => i.Id == item.Id);
            saved.Closure!.State.Should().Be(ShiftClosureState.Closed);
            saved.ShiftLeadId.Should().BeOneOf(first.Id, second.Id);
            (await FallbackManagerIdAsync(admin)).Should().Be(second.Id);
        }
        finally
        {
            await DeleteDayAsync(date);
            await ArchiveUsersAsync(first.Id, second.Id);
        }
    }

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> AdminClientAsync() => ClientAsync("admin", "Admin123!");

    private async Task<TestUser> SeedUserAsync(
        UserRole role, DateTime createdAt, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"manager_fallback_{role}_{Guid.NewGuid():N}";
        const string password = "Passw0rd!";
        var user = new User
        {
            UserName = login,
            Email = $"{login}@test.local",
            FullName = login,
            EmployeeId = $"EMP-{Guid.NewGuid():N}"[..12],
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password);
    }

    private async Task<RaceResult> UpdateUnderScheduleLockAsync(
        HttpClient admin,
        string userId,
        UpdatePersonnelRequestDto request,
        Func<Task<HttpResponseMessage>>? operation = null,
        Func<Task>? beforeRelease = null)
    {
        var connectionString = await ConnectionStringAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
                         $"SELECT pg_advisory_xact_lock({ScheduleLockKey})", connection, transaction))
            await command.ExecuteNonQueryAsync();

        var update = admin.PutAsJsonAsync($"/api/personnel/{userId}", request);
        var concurrentOperation = operation?.Invoke();
        var expectedWaiters = concurrentOperation == null ? 1 : 2;
        var bothWaited = false;
        try
        {
            for (var attempt = 0; attempt < 200 && !bothWaited; attempt++)
            {
                if (update.IsCompleted || concurrentOperation?.IsCompleted == true)
                    break;
                bothWaited = await AdvisoryWaitersAsync(connection, transaction) >= expectedWaiters;
                if (!bothWaited) await Task.Delay(25);
            }
            if (beforeRelease != null) await beforeRelease();
        }
        finally
        {
            await transaction.CommitAsync();
        }

        var updateResponse = await update;
        var operationResponse = concurrentOperation == null ? null : await concurrentOperation;
        return new RaceResult(updateResponse, operationResponse, bothWaited);
    }

    private static async Task<int> AdvisoryWaitersAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var classId = (uint)((ulong)ScheduleLockKey >> 32);
        var objectId = unchecked((uint)ScheduleLockKey);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_locks " +
            "WHERE locktype = 'advisory' AND NOT granted " +
            $"AND classid = {classId} AND objid = {objectId} " +
            "AND pid <> pg_backend_pid()",
            connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> ConnectionStringAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return db.Database.GetDbConnection().ConnectionString;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private async Task<string?> FallbackManagerIdAsync(HttpClient client)
    {
        var body = await RequiredJsonAsync(await client.GetAsync("/api/shifts/default-leads"));
        var fallback = body.GetProperty("fallbackManager");
        return fallback.ValueKind == JsonValueKind.Null
            ? null
            : fallback.GetProperty("id").GetString();
    }

    private static async Task<JsonElement> GetDayAsync(HttpClient client, DateOnly date)
    {
        var days = await RequiredJsonAsync(await client.GetAsync(
            $"/api/shift-plan?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}"));
        return days.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static JsonElement Item(JsonElement day, int position) =>
        day.GetProperty("items").EnumerateArray()
            .Should().ContainSingle(i => i.GetProperty("position").GetInt32() == position).Subject;

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

    private async Task<ShiftPlanItem> SeedItemAsync(DateTime start, DateTime end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var day = new ShiftPlanDay { ShiftDate = NewDate() };
        var item = new ShiftPlanItem
        {
            Position = Interlocked.Increment(ref _positionOffset),
            StartAtUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
            EndAtUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = null,
            Day = day
        };
        day.Items.Add(item);
        db.ShiftPlanDays.Add(day);
        await db.SaveChangesAsync();
        return item;
    }

    private static (DateTime Start, DateTime End) PastWindow()
    {
        var value = DateTime.UtcNow.AddDays(-200 - 2 * Interlocked.Increment(ref _windowOffset));
        var anchor = new DateTime(value.Year, value.Month, value.Day,
            value.Hour, value.Minute, value.Second, DateTimeKind.Utc);
        return (anchor.AddHours(-4), anchor.AddHours(-2));
    }

    private static DateOnly NewDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset));

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
    }

    private async Task ArchiveUsersAsync(params string[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Users.Where(u => ids.Contains(u.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
    }

    private async Task<List<ManagerState>> DeactivateOtherManagersAsync(string exceptId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var previous = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Manager && u.Id != exceptId)
            .Select(u => new ManagerState(u.Id, u.Role, u.IsActive))
            .ToListAsync();
        await db.Users.Where(u => u.Role == UserRole.Manager && u.Id != exceptId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        return previous;
    }

    private async Task RestoreManagersAsync(IEnumerable<ManagerState> states)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        foreach (var state in states)
        {
            await db.Users.Where(u => u.Id == state.Id)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.Role, state.Role)
                    .SetProperty(x => x.IsActive, state.IsActive));
        }
    }
}
