using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Planning;

[Collection("Integration")]
public class ShiftPlanMaterializationApiTests : IClassFixture<IntegrationTestFactory>
{
    private const long ScheduleLockKey = 0x5754_5343_4845_4455;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 300;

    private readonly IntegrationTestFactory _factory;

    public ShiftPlanMaterializationApiTests(IntegrationTestFactory factory) => _factory = factory;

    private sealed record TestUser(string Id, string Login, string Password, string FullName);

    private sealed class MaterializationGate
    {
        public TaskCompletionSource<ShiftPlanDay> Committed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingShiftPlanMaterializer : IShiftPlanMaterializer
    {
        private readonly IShiftPlanMaterializer _inner;
        private readonly MaterializationGate _gate;

        public BlockingShiftPlanMaterializer(
            IShiftPlanMaterializer inner, MaterializationGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public async Task<ShiftPlanDay> MaterializeAsync(
            DateOnly date, int position, CancellationToken ct)
        {
            var day = await _inner.MaterializeAsync(date, position, ct);
            _gate.Committed.TrySetResult(day);
            await _gate.Release.Task.WaitAsync(ct);
            return day;
        }

        public Task<ShiftPlanDay> LoadTrackedDayForUpdateAsync(
            DateOnly date, CancellationToken ct) =>
            _inner.LoadTrackedDayForUpdateAsync(date, ct);
    }

    private static DateOnly NewDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset));

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
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"shift_materialize_{role}_{Guid.NewGuid():N}";
        const string password = "Passw0rd!";
        var fullName = $"{role} {Guid.NewGuid():N}";
        var user = new User
        {
            UserName = login,
            Email = $"{login}@test.local",
            FullName = fullName,
            EmployeeId = $"EMP-{Guid.NewGuid():N}"[..12],
            Role = role,
            IsActive = true
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password, fullName);
    }

    private async Task<string> FirstActiveManagerIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstAsync();
    }

    private static Task<HttpResponseMessage> MaterializeAsync(
        HttpClient client, DateOnly date, int position) =>
        client.PostAsync($"/api/shift-plan/days/{date:yyyy-MM-dd}/items/{position}/materialize", null);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
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

    private static List<ShiftScheduleItemDto> OneShift() =>
    [
        new() { StartMinutes = 480, DurationMinutes = 480, TimeZoneId = "Europe/Moscow" }
    ];

    private static List<ShiftScheduleItemDto> TwoShifts() =>
    [
        new() { StartMinutes = 480, DurationMinutes = 480, TimeZoneId = "Europe/Moscow" },
        new() { StartMinutes = 960, DurationMinutes = 480, TimeZoneId = "Europe/Moscow" }
    ];

    private static Task<HttpResponseMessage> SaveScheduleAsync(
        HttpClient client, List<ShiftScheduleItemDto> shifts) =>
        client.PutAsJsonAsync("/api/shifts", new SaveShiftsRequestDto { Shifts = shifts });

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

    private async Task<string?> SetDefaultAsync(int position, string? leadId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var existing = await db.ShiftDefaultLeads.SingleOrDefaultAsync(d => d.Position == position);
        var previous = existing?.ShiftLeadId;
        if (existing != null)
            db.ShiftDefaultLeads.Remove(existing);
        if (leadId != null)
            db.ShiftDefaultLeads.Add(new ShiftDefaultLead { Position = position, ShiftLeadId = leadId });
        await db.SaveChangesAsync();
        return previous;
    }

    private async Task RestoreDefaultAsync(int position, string? leadId) =>
        await SetDefaultAsync(position, leadId);

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
    }

    private async Task<NpgsqlTransaction> HoldScheduleLockAsync(NpgsqlConnection connection)
    {
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT pg_advisory_xact_lock({ScheduleLockKey})", connection, transaction);
        await command.ExecuteNonQueryAsync();
        return transaction;
    }

    private static async Task WaitForAdvisoryWaitersAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, int expected)
    {
        var waiting = 0;
        for (var attempt = 0; attempt < 200 && waiting < expected; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
                connection, transaction);
            waiting = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waiting < expected) await Task.Delay(25);
        }
        waiting.Should().Be(expected);
    }

    [Fact]
    public async Task Materialize_DefaultLead_PersistsFullDayWithoutExplicitLeadOrSideEffectsAndIsIdempotent()
    {
        var defaultLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var client = await AdminClientAsync();
        var previousDefault = await SetDefaultAsync(1, defaultLead.Id);
        try
        {
            await WithScheduleAsync(client, TwoShifts(), async () =>
            {
                int taskCount;
                using (var scope = _factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                    taskCount = await db.ShiftTasks.CountAsync();
                }

                var first = await RequiredJsonAsync(await MaterializeAsync(client, date, 1));
                first.GetProperty("shiftDate").GetString().Should().Be(date.ToString("yyyy-MM-dd"));
                first.GetProperty("isSaved").GetBoolean().Should().BeTrue();
                var firstItems = first.GetProperty("items").EnumerateArray().ToList();
                firstItems.Select(i => i.GetProperty("position").GetInt32()).Should().Equal(1, 2);
                firstItems.Should().OnlyContain(i => i.GetProperty("id").ValueKind == JsonValueKind.String);
                Item(first, 1).GetProperty("shiftLeadId").GetString().Should().Be(defaultLead.Id);
                Item(first, 1).GetProperty("shiftLeadSource").GetString().Should().Be("Default");
                Item(first, 2).GetProperty("shiftLeadSource").GetString().Should().Be("ManagerFallback");

                var second = await RequiredJsonAsync(await MaterializeAsync(client, date, 1));
                second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())
                    .Should().Equal(firstItems.Select(i => i.GetProperty("id").GetGuid()));

                using var verifyScope = _factory.Services.CreateScope();
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var savedItems = await verifyDb.ShiftPlanItems.AsNoTracking()
                    .Where(i => i.Day.ShiftDate == date).OrderBy(i => i.Position).ToListAsync();
                savedItems.Should().HaveCount(2);
                savedItems.Should().OnlyContain(i => i.ShiftLeadId == null,
                    "материализация не превращает default/fallback в явный override");
                var itemIds = savedItems.Select(i => i.Id).ToList();
                (await verifyDb.ShiftClosures.CountAsync(c => itemIds.Contains(c.ShiftPlanItemId))).Should().Be(0);
                (await verifyDb.ShiftImmOperatorAssignments.CountAsync(a => itemIds.Contains(a.ShiftPlanItemId)))
                    .Should().Be(0);
                (await verifyDb.ShiftTasks.CountAsync()).Should().Be(taskCount);
            });
        }
        finally
        {
            await DeleteDayAsync(date);
            await RestoreDefaultAsync(1, previousDefault);
        }
    }

    [Fact]
    public async Task Materialize_ManagerFallbackOnSavedDay_PreservesIdsWindowsExplicitLeadAndOperationalData()
    {
        var explicitLead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var managerId = await FirstActiveManagerIdAsync();
        var date = NewDate();
        var now = DateTime.UtcNow;
        var client = await ManagerClientAsync();
        Guid firstItemId;
        Guid secondItemId;
        DateTime firstStart;
        DateTime firstEnd;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var day = new ShiftPlanDay { ShiftDate = date };
            var first = new ShiftPlanItem
            {
                Position = 1,
                StartAtUtc = now.AddHours(2),
                EndAtUtc = now.AddHours(3),
                TimeZoneId = "Europe/Moscow",
                ShiftLeadId = explicitLead.Id
            };
            var second = new ShiftPlanItem
            {
                Position = 2,
                StartAtUtc = now.AddHours(4),
                EndAtUtc = now.AddHours(5),
                TimeZoneId = "Europe/Moscow"
            };
            day.Items.Add(first);
            day.Items.Add(second);
            db.ShiftPlanDays.Add(day);
            await db.SaveChangesAsync();
            db.ShiftImmOperatorAssignments.Add(new ShiftImmOperatorAssignment
            {
                ShiftPlanItemId = first.Id,
                ImmId = _factory.TestImmId,
                OperatorId = @operator.Id,
                Slot = 1,
                AssignedAt = first.StartAtUtc,
                CreatedById = explicitLead.Id
            });
            db.ShiftClosures.Add(new ShiftClosure
            {
                ShiftPlanItemId = first.Id,
                State = ShiftClosureState.Draft,
                CreatedById = explicitLead.Id
            });
            await db.SaveChangesAsync();
            firstItemId = first.Id;
            secondItemId = second.Id;
            firstStart = first.StartAtUtc;
            firstEnd = first.EndAtUtc;
        }

        try
        {
            var result = await RequiredJsonAsync(await MaterializeAsync(client, date, 2));
            Item(result, 1).GetProperty("id").GetGuid().Should().Be(firstItemId);
            Item(result, 2).GetProperty("id").GetGuid().Should().Be(secondItemId);
            Item(result, 1).GetProperty("startAtUtc").GetDateTime().Should()
                .BeCloseTo(firstStart, TimeSpan.FromMicroseconds(1));
            Item(result, 1).GetProperty("endAtUtc").GetDateTime().Should()
                .BeCloseTo(firstEnd, TimeSpan.FromMicroseconds(1));
            Item(result, 1).GetProperty("shiftLeadId").GetString().Should().Be(explicitLead.Id);
            Item(result, 1).GetProperty("shiftLeadSource").GetString().Should().Be("Explicit");
            Item(result, 1).GetProperty("operatorAssignmentCount").GetInt32().Should().Be(1);
            Item(result, 1).GetProperty("closureStatus").GetString().Should().Be("Draft");
            Item(result, 2).GetProperty("shiftLeadId").GetString().Should().Be(managerId);
            Item(result, 2).GetProperty("shiftLeadSource").GetString().Should().Be("ManagerFallback");

            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await verifyDb.ShiftPlanItems.SingleAsync(i => i.Id == firstItemId)).ShiftLeadId
                .Should().Be(explicitLead.Id);
            (await verifyDb.ShiftImmOperatorAssignments.CountAsync(a => a.ShiftPlanItemId == firstItemId))
                .Should().Be(1);
            (await verifyDb.ShiftClosures.CountAsync(c => c.ShiftPlanItemId == firstItemId)).Should().Be(1);
        }
        finally
        {
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task Materialize_WithoutEffectiveLead_Returns400AndDoesNotSaveDay()
    {
        var date = NewDate();
        var client = await ManagerClientAsync();
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);
        await WithScheduleAsync(client, TwoShifts(), async () =>
        {
            var response = await MaterializeAsync(client, date, 1);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await JsonAsync(response)).GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(0);
        });
    }

    [Fact]
    public async Task Materialize_UnknownPosition_Returns404()
    {
        var date = NewDate();
        var client = await ManagerClientAsync();

        var response = await MaterializeAsync(client, date, 99);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(response)).GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Materialize_IsForbiddenForNonManagerRoles()
    {
        foreach (var role in new[] { UserRole.ShiftLead, UserRole.Adjuster, UserRole.Observer, UserRole.Operator })
        {
            var user = await SeedUserAsync(role);
            var client = await ClientAsync(user.Login, user.Password);

            (await MaterializeAsync(client, NewDate(), 1)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }
    }

    [Fact]
    public async Task Get_VirtualNearestWithEffectiveLead_CanEditOperatorsBeforeMaterialization()
    {
        var client = await ManagerClientAsync();
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        var schedule = new List<ShiftScheduleItemDto>
        {
            new()
            {
                StartMinutes = ((localNow.Hour * 60 + localNow.Minute - 60) % 1440 + 1440) % 1440,
                DurationMinutes = 180,
                TimeZoneId = "Europe/Moscow"
            },
            new()
            {
                StartMinutes = (localNow.Hour * 60 + localNow.Minute + 180) % 1440,
                DurationMinutes = 60,
                TimeZoneId = "Europe/Moscow"
            }
        };
        var today = DateOnly.FromDateTime(localNow)
            .AddDays(schedule[0].StartMinutes > localNow.Hour * 60 + localNow.Minute ? -1 : 0);
        await DeleteDayAsync(today);
        try
        {
            await WithScheduleAsync(client, schedule, async () =>
            {
                var day = await GetDayAsync(client, today);
                day.GetProperty("isSaved").GetBoolean().Should().BeFalse();
                var current = day.GetProperty("items").EnumerateArray()
                    .Single(i => i.GetProperty("state").GetString() == "Current");
                current.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
                current.GetProperty("shiftLeadId").ValueKind.Should().Be(JsonValueKind.String);
                current.GetProperty("canEditOperators").GetBoolean().Should().BeTrue();
            });
        }
        finally
        {
            await DeleteDayAsync(today);
        }
    }

    [Fact]
    public async Task Materialize_DefaultClearedAfterCommit_Returns409InsteadOfDeletedIds()
    {
        var defaultLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var admin = await AdminClientAsync();
        var gate = new MaterializationGate();
        await using var noFallback = await NoLeadFallbackScope.CreateAsync(_factory);
        await SetDefaultAsync(1, defaultLead.Id);
        try
        {
            await WithScheduleAsync(admin, OneShift(), async () =>
            {
                await using var raceFactory = _factory.WithWebHostBuilder(builder =>
                    builder.ConfigureServices(services =>
                    {
                        services.AddScoped<IShiftPlanMaterializer>(sp =>
                            new BlockingShiftPlanMaterializer(
                                new ShiftPlanMaterializer(
                                    sp.GetRequiredService<ControlDbContext>(),
                                    sp.GetRequiredService<IShiftLeadFallbackProvider>()),
                                gate));
                    }));
                using var client = raceFactory.CreateClient();
                var token = await AuthHelper.GetTokenAsync(client, "admin", "Admin123!");
                AuthHelper.SetBearerToken(client, token!);

                var request = MaterializeAsync(client, date, 1);
                try
                {
                    var committedDay = await gate.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    committedDay.Items.Should().NotBeEmpty();

                    var clear = await admin.PutAsJsonAsync("/api/shifts/default-leads", new
                    {
                        items = new[] { new { position = 1, shiftLeadId = (string?)null } }
                    });
                    clear.StatusCode.Should().Be(HttpStatusCode.OK,
                        await clear.Content.ReadAsStringAsync());
                    using var scope = _factory.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                    (await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(0);
                }
                finally
                {
                    gate.Release.TrySetResult();
                }

                var response = await request;
                response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                    "после удаления материализованного дня нельзя вернуть его прежние Id: " +
                    await response.Content.ReadAsStringAsync());
            });
        }
        finally
        {
            await DeleteDayAsync(date);
            await SetDefaultAsync(1, null);
        }
    }

    [Fact]
    public async Task ConcurrentMaterialize_ReturnsOneSuccessAndOne409WithoutDuplicates()
    {
        var date = NewDate();
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }

        try
        {
            await WithScheduleAsync(manager, TwoShifts(), async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var transaction = await HoldScheduleLockAsync(connection);
                var requests = Task.WhenAll(
                    MaterializeAsync(manager, date, 1),
                    MaterializeAsync(admin, date, 1));
                await WaitForAdvisoryWaitersAsync(connection, transaction, 2);
                await transaction.CommitAsync();
                var responses = await requests;

                responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
                responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
                var saved = await GetDayAsync(manager, date);
                saved.GetProperty("items").EnumerateArray().Should().HaveCount(2);
                using var verifyScope = _factory.Services.CreateScope();
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
                (await verifyDb.ShiftPlanDays.CountAsync(d => d.ShiftDate == date)).Should().Be(1);
                (await verifyDb.ShiftPlanItems.CountAsync(i => i.Day.ShiftDate == date)).Should().Be(2);
            });
        }
        finally
        {
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task ConcurrentDefaultChangeAndMaterialize_ReturnsConsistentLeadOr409()
    {
        var oldLead = await SeedUserAsync(UserRole.ShiftLead);
        var newLead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewDate();
        var manager = await ManagerClientAsync();
        var previousDefault = await SetDefaultAsync(1, oldLead.Id);
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }

        try
        {
            await WithScheduleAsync(manager, TwoShifts(), async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var transaction = await HoldScheduleLockAsync(connection);
                var defaultRequest = manager.PutAsJsonAsync("/api/shifts/default-leads", new
                {
                    items = new[]
                    {
                        new { position = 1, shiftLeadId = (string?)newLead.Id },
                        new { position = 2, shiftLeadId = (string?)null }
                    }
                });
                await WaitForAdvisoryWaitersAsync(connection, transaction, 1);
                var materializeRequest = MaterializeAsync(manager, date, 1);
                await WaitForAdvisoryWaitersAsync(connection, transaction, 2);
                await transaction.CommitAsync();
                (await defaultRequest).StatusCode.Should().Be(HttpStatusCode.OK);
                var materializeResponse = await materializeRequest;

                materializeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
                if (materializeResponse.StatusCode == HttpStatusCode.OK)
                {
                    var result = await JsonAsync(materializeResponse);
                    Item(result, 1).GetProperty("shiftLeadId").GetString()
                        .Should().BeOneOf(oldLead.Id, newLead.Id);
                    Item(result, 1).GetProperty("shiftLeadSource").GetString().Should().Be("Default");
                }
                var current = await GetDayAsync(manager, date);
                Item(current, 1).GetProperty("shiftLeadId").GetString().Should().Be(newLead.Id);
                using var verifyScope = _factory.Services.CreateScope();
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var savedItems = await verifyDb.ShiftPlanItems.Where(i => i.Day.ShiftDate == date).ToListAsync();
                savedItems.Should().OnlyContain(i => i.ShiftLeadId == null);
            });
        }
        finally
        {
            await DeleteDayAsync(date);
            await RestoreDefaultAsync(1, previousDefault);
        }
    }

    [Fact]
    public async Task ConcurrentScheduleChangeAndMaterialize_ReturnsConsistentWindowsOr409()
    {
        var date = NewDate();
        var manager = await ManagerClientAsync();
        var oldSchedule = TwoShifts();
        var newSchedule = TwoShifts();
        newSchedule[0].StartMinutes = 420;
        newSchedule[0].DurationMinutes = 540;
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }

        try
        {
            await WithScheduleAsync(manager, oldSchedule, async () =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var transaction = await HoldScheduleLockAsync(connection);
                var scheduleRequest = SaveScheduleAsync(manager, newSchedule);
                await WaitForAdvisoryWaitersAsync(connection, transaction, 1);
                var materializeRequest = MaterializeAsync(manager, date, 1);
                await WaitForAdvisoryWaitersAsync(connection, transaction, 2);
                await transaction.CommitAsync();
                (await scheduleRequest).StatusCode.Should().Be(HttpStatusCode.OK);
                var materializeResponse = await materializeRequest;

                materializeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
                if (materializeResponse.StatusCode == HttpStatusCode.OK)
                {
                    var result = await JsonAsync(materializeResponse);
                    var item = Item(result, 1);
                    var actual = (Start: item.GetProperty("startAtUtc").GetDateTime(),
                        End: item.GetProperty("endAtUtc").GetDateTime());
                    var oldWindows = Wintime.Control.Core.Policies.ShiftWindowCalculator.Build(
                        date, oldSchedule.Select(s => new Shift
                        {
                            StartMinutes = s.StartMinutes,
                            DurationMinutes = s.DurationMinutes,
                            TimeZoneId = s.TimeZoneId
                        }).ToList());
                    var newWindows = Wintime.Control.Core.Policies.ShiftWindowCalculator.Build(
                        date, newSchedule.Select(s => new Shift
                        {
                            StartMinutes = s.StartMinutes,
                            DurationMinutes = s.DurationMinutes,
                            TimeZoneId = s.TimeZoneId
                        }).ToList());
                    actual.Should().BeOneOf(
                        (oldWindows[0].StartAtUtc, oldWindows[0].EndAtUtc),
                        (newWindows[0].StartAtUtc, newWindows[0].EndAtUtc));
                }
                var current = await GetDayAsync(manager, date);
                var expectedStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue),
                    TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow").GetUtcOffset(date.ToDateTime(TimeOnly.MinValue)))
                    .UtcDateTime.AddMinutes(newSchedule[0].StartMinutes);
                Item(current, 1).GetProperty("startAtUtc").GetDateTime().Should().Be(expectedStart);
            });
        }
        finally
        {
            await DeleteDayAsync(date);
        }
    }
}
