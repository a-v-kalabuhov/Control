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
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Planning;

[Collection("Integration")]
public class ShiftDefaultLeadsApiTests : IClassFixture<IntegrationTestFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static int _dateOffset = 600;

    private readonly IntegrationTestFactory _factory;

    public ShiftDefaultLeadsApiTests(IntegrationTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => ClearDefaultsIfTableExistsAsync();
    public Task DisposeAsync() => ClearDefaultsIfTableExistsAsync();

    private sealed record TestUser(string Id, string Login, string Password);
    private sealed record DefaultRow(int Position, string ShiftLeadId);
    private sealed record ItemSnapshot(
        Guid ItemId, int Position, Guid? AssignmentId, Guid? ClosureId, ShiftClosureState? ClosureState);

    [Fact]
    public async Task GetPut_RoundTripReturnsScheduleAndFallbackAndAllowsSameLeadOnSeveralPositions()
    {
        var manager = await ManagerClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);

        await WithScheduleAsync(manager, Schedule(480, 960), async () =>
        {
            var put = await PutDefaultsAsync(manager, (1, lead.Id), (2, lead.Id));
            var body = await RequiredJsonAsync(put);
            AssertSettingsBody(body, expectedCount: 2);

            var items = body.GetProperty("items").EnumerateArray().ToList();
            items.Select(i => i.GetProperty("position").GetInt32()).Should().Equal(1, 2);
            items.Select(i => i.GetProperty("number").GetInt32()).Should().Equal(1, 2);
            items.Select(i => i.GetProperty("startMinutes").GetInt32()).Should().Equal(480, 960);
            items.Should().OnlyContain(i => i.GetProperty("durationMinutes").GetInt32() == 480);
            items.Should().OnlyContain(i => i.GetProperty("timeZoneId").GetString() == "Europe/Moscow");
            items.Should().OnlyContain(i => i.GetProperty("defaultShiftLeadId").GetString() == lead.Id);
            items.Should().OnlyContain(i => i.GetProperty("defaultShiftLeadName").GetString() == lead.Login);
            items.Should().OnlyContain(i => i.GetProperty("defaultShiftLeadEmployeeId").GetString() != null);

            var expectedFallback = await FirstActiveManagerAsync();
            var fallback = body.GetProperty("fallbackManager");
            fallback.GetProperty("id").GetString().Should().Be(expectedFallback!.Id);
            fallback.GetProperty("fullName").GetString().Should().Be(expectedFallback.FullName);
            fallback.GetProperty("employeeId").GetString().Should().Be(expectedFallback.EmployeeId);
            fallback.GetProperty("createdAt").GetDateTime().Should().Be(expectedFallback.CreatedAt);

            var get = await RequiredJsonAsync(await manager.GetAsync("/api/shifts/default-leads"));
            get.GetProperty("items").GetRawText().Should().Be(body.GetProperty("items").GetRawText());
            (await DefaultRowsAsync()).Should().Equal(
                new DefaultRow(1, lead.Id), new DefaultRow(2, lead.Id));
        });
    }

    [Fact]
    public async Task FallbackManager_UsesCreatedAtThenIdAndSkipsInactiveAndOtherRoles()
    {
        var admin = await AdminClientAsync();
        var deactivated = await DeactivateActiveManagersAsync();
        try
        {
            var tiedCreatedAt = DateTime.UtcNow.AddYears(-5);
            var first = await SeedUserAsync(UserRole.Manager, id: "default-lead-manager-a", createdAt: tiedCreatedAt);
            var second = await SeedUserAsync(UserRole.Manager, id: "default-lead-manager-b", createdAt: tiedCreatedAt);
            await SeedUserAsync(UserRole.Manager, id: "default-lead-manager-later", createdAt: tiedCreatedAt.AddDays(1));
            await SeedUserAsync(UserRole.Manager, id: "default-lead-manager-inactive", createdAt: tiedCreatedAt.AddDays(-1), isActive: false);
            await SeedUserAsync(UserRole.Admin, id: "default-lead-admin-earlier", createdAt: tiedCreatedAt.AddDays(-2));

            var firstBody = await RequiredJsonAsync(await admin.GetAsync("/api/shifts/default-leads"));
            firstBody.GetProperty("fallbackManager").GetProperty("id").GetString().Should().Be(first.Id);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                await db.Users.Where(u => u.Id == first.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
            }

            var secondBody = await RequiredJsonAsync(await admin.GetAsync("/api/shifts/default-leads"));
            secondBody.GetProperty("fallbackManager").GetProperty("id").GetString().Should().Be(second.Id);
        }
        finally
        {
            await RestoreManagersAsync(deactivated);
        }
    }

    [Fact]
    public async Task SettingsAuthorization_AndCandidates_UseDomainRoles()
    {
        var manager = await ManagerClientAsync();
        var admin = await AdminClientAsync();
        var adjuster = await ClientAsync("test_adjuster", "Adjuster123!");
        var observer = await ClientAsync("test_observer", "Observer123!");
        var shiftLead = await SeedUserAsync(UserRole.ShiftLead);
        var shiftLeadClient = await ClientAsync(shiftLead.Login, shiftLead.Password);
        var activeManager = await SeedUserAsync(UserRole.Manager);
        var adminCandidate = await SeedUserAsync(UserRole.Admin);
        var operatorCandidate = await SeedUserAsync(UserRole.Operator);
        var archivedLead = await SeedUserAsync(UserRole.ShiftLead, isActive: false);

        foreach (var client in new[] { manager, admin })
            (await client.GetAsync("/api/shifts/default-leads")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var client in new[] { adjuster, observer, shiftLeadClient })
        {
            (await client.GetAsync("/api/shifts/default-leads")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await PutDefaultsAsync(client)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var candidates = await RequiredJsonAsync(await manager.GetAsync("/api/shift-plan/lead-candidates"));
        var candidateIds = candidates.EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToHashSet();
        candidateIds.Should().Contain(shiftLead.Id);
        candidateIds.Should().Contain(activeManager.Id);
        candidateIds.Should().NotContain(adminCandidate.Id);
        candidateIds.Should().NotContain(operatorCandidate.Id);
        candidateIds.Should().NotContain(archivedLead.Id);
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Operator, true)]
    [InlineData(UserRole.ShiftLead, false)]
    [InlineData(null, true)]
    public async Task Put_InvalidCandidate_Returns400AndDoesNotChangeExistingDefaults(
        UserRole? role, bool isActive)
    {
        var manager = await ManagerClientAsync();
        var original = await SeedUserAsync(UserRole.ShiftLead);
        var other = await SeedUserAsync(UserRole.ShiftLead);
        var invalidId = role == null
            ? $"missing-{Guid.NewGuid():N}"
            : (await SeedUserAsync(role.Value, isActive: isActive)).Id;

        await WithScheduleAsync(manager, Schedule(480, 960), async () =>
        {
            (await PutDefaultsAsync(manager, (1, original.Id), (2, null))).StatusCode.Should().Be(HttpStatusCode.OK);
            var response = await PutDefaultsAsync(manager, (1, invalidId), (2, other.Id));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, original.Id));
        });
    }

    [Fact]
    public async Task Put_NullItem_Returns400AndDoesNotChangeExistingDefaults()
    {
        var manager = await ManagerClientAsync();
        var original = await SeedUserAsync(UserRole.ShiftLead);

        await WithScheduleAsync(manager, Schedule(480), async () =>
        {
            (await PutDefaultsAsync(manager, (1, original.Id))).StatusCode.Should().Be(HttpStatusCode.OK);

            var response = await manager.PutAsJsonAsync("/api/shifts/default-leads", new
            {
                items = new object?[] { null }
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, original.Id));
        });
    }

    [Fact]
    public async Task Put_StaleOrDuplicatePositions_Returns409AndDoesNotChangeExistingDefaults()
    {
        var manager = await ManagerClientAsync();
        var original = await SeedUserAsync(UserRole.ShiftLead);
        var replacement = await SeedUserAsync(UserRole.ShiftLead);

        await WithScheduleAsync(manager, Schedule(480, 960), async () =>
        {
            (await PutDefaultsAsync(manager, (1, original.Id), (2, null))).StatusCode.Should().Be(HttpStatusCode.OK);

            var stale = await PutDefaultsAsync(manager, (1, replacement.Id), (3, replacement.Id));
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync());
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, original.Id));

            var duplicate = await PutDefaultsAsync(manager, (1, replacement.Id), (1, null));
            duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict, await duplicate.Content.ReadAsStringAsync());
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, original.Id));
        });
    }

    [Fact]
    public async Task ClearingDefault_RequiresEffectiveLeadForOperationalDataAndPreservesThatData()
    {
        var admin = await AdminClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var date = NewFutureDate();
        var deactivated = await DeactivateActiveManagersAsync();
        try
        {
            await WithScheduleAsync(admin, Schedule(480), async () =>
            {
                await SeedOperationalDayAsync(date, lead.Id, @operator.Id);
                (await PutDefaultsAsync(admin, (1, lead.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
                var before = await ItemSnapshotAsync(date, position: 1);

                var rejected = await PutDefaultsAsync(admin, (1, null));
                rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest, await rejected.Content.ReadAsStringAsync());
                (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, lead.Id));
                (await ItemSnapshotAsync(date, position: 1)).Should().Be(before);

                await RestoreManagersAsync(deactivated);
                deactivated = [];
                var accepted = await PutDefaultsAsync(admin, (1, null));
                accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
                (await DefaultRowsAsync()).Should().BeEmpty();
                (await ItemSnapshotAsync(date, position: 1)).Should().Be(before);
            });
        }
        finally
        {
            await RestoreManagersAsync(deactivated);
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task ClearingDefault_WithoutManager_DeletesEmptyMaterializedDay()
    {
        var admin = await AdminClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewFutureDate();
        var deactivated = await DeactivateActiveManagersAsync();
        try
        {
            await WithScheduleAsync(admin, Schedule(480), async () =>
            {
                await SeedEmptyDayAsync(date);
                (await PutDefaultsAsync(admin, (1, lead.Id))).StatusCode.Should().Be(HttpStatusCode.OK);

                var response = await PutDefaultsAsync(admin, (1, null));

                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                (await OperationalDayCountAsync(date)).Should().Be(0);
            });
        }
        finally
        {
            await RestoreManagersAsync(deactivated);
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task ScheduleReplacement_KeepsPositionsWithinRangeAndDeletesDefaultsBeyondRange()
    {
        var manager = await ManagerClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);

        await WithScheduleAsync(manager, Schedule(0, 480, 960), async () =>
        {
            (await PutDefaultsAsync(manager, (1, lead.Id), (2, null), (3, lead.Id)))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            (await SaveScheduleAsync(manager, Schedule(120))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, lead.Id));

            (await SaveScheduleAsync(manager, Schedule(120, 600))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, lead.Id));
        });
    }

    [Fact]
    public async Task ScheduleReplacement_DefaultOnlyFutureDayIsPreservedAndSynchronized()
    {
        var manager = await ManagerClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewFutureDate();

        try
        {
            await WithScheduleAsync(manager, Schedule(480), async () =>
            {
                await SeedEmptyDayAsync(date);
                (await PutDefaultsAsync(manager, (1, lead.Id))).StatusCode.Should().Be(HttpStatusCode.OK);

                var response = await SaveScheduleAsync(manager, Schedule(600));

                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                (await OperationalDayCountAsync(date)).Should().Be(1);
                var expected = await CurrentWindowAsync(date, 1);
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var item = await db.ShiftPlanItems.AsNoTracking().SingleAsync(i => i.Day.ShiftDate == date);
                item.StartAtUtc.Should().Be(expected.StartAtUtc);
                item.EndAtUtc.Should().Be(expected.EndAtUtc);
                (await DefaultRowsAsync()).Should().Equal(new DefaultRow(1, lead.Id));
            });
        }
        finally
        {
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task ScheduleReplacement_AfterDefaultClear_PreservesManagerFallbackOperationalSnapshot()
    {
        var manager = await ManagerClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var @operator = await SeedUserAsync(UserRole.Operator);
        var date = NewFutureDate();

        try
        {
            await WithScheduleAsync(manager, Schedule(480), async () =>
            {
                await SeedOperationalDayAsync(date, lead.Id, @operator.Id);
                (await PutDefaultsAsync(manager, (1, lead.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
                (await PutDefaultsAsync(manager, (1, null))).StatusCode.Should().Be(HttpStatusCode.OK);
                (await DefaultRowsAsync()).Should().BeEmpty();
                var before = await ItemSnapshotAsync(date, position: 1);
                var changedSchedule = Schedule(480);
                changedSchedule[0].BreakStartMinutes += 10;

                var response = await SaveScheduleAsync(manager, changedSchedule);

                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                var after = await ItemSnapshotAsync(date, position: 1);
                after.Should().Be(before, "ManagerFallback и операционные данные сохраняют материализованный день");
                after.ClosureState.Should().Be(ShiftClosureState.Draft);
                (await DefaultRowsAsync()).Should().BeEmpty();
            });
        }
        finally
        {
            await DeleteDayAsync(date);
        }
    }

    [Fact]
    public async Task ScheduleReplacementFailure_RollsBackSchedulePlanAndDefaultsTogether()
    {
        var manager = await ManagerClientAsync();
        var lead = await SeedUserAsync(UserRole.ShiftLead);
        var date = NewFutureDate();

        await WithScheduleAsync(manager, Schedule(480, 960), async () =>
        {
            (await PutDefaultsAsync(manager, (1, null), (2, lead.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
            await SeedDraftOnSecondPositionAsync(date, lead.Id);
            var scheduleBefore = await ScheduleKeysAsync();
            var secondPositionBefore = await ItemSnapshotAsync(date, position: 2);

            var response = await SaveScheduleAsync(manager, Schedule(120));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await ScheduleKeysAsync()).Should().Equal(scheduleBefore);
            (await DefaultRowsAsync()).Should().Equal(new DefaultRow(2, lead.Id));
            var secondPositionAfter = await ItemSnapshotAsync(date, position: 2);
            secondPositionAfter.Should().Be(secondPositionBefore,
                "rollback сохраняет вторую позицию и её Draft рапорт с прежними Id");
            secondPositionAfter.ClosureState.Should().Be(ShiftClosureState.Draft);
            await DeleteDayAsync(date);
        });
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

    private async Task<TestUser> SeedUserAsync(
        UserRole role, string? id = null, DateTime? createdAt = null, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"default_leads_{role}_{Guid.NewGuid():N}";
        const string password = "Passw0rd!";
        var user = new User
        {
            Id = id ?? Guid.NewGuid().ToString(),
            UserName = login,
            Email = $"{login}@test.local",
            FullName = login,
            EmployeeId = $"E{Guid.NewGuid():N}"[..12],
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTime.SpecifyKind(createdAt ?? DateTime.UtcNow, DateTimeKind.Utc)
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        return new TestUser(user.Id, login, password);
    }

    private static List<ShiftScheduleItemDto> Schedule(params int[] startMinutes) =>
        startMinutes.Select(start => new ShiftScheduleItemDto
        {
            StartMinutes = start,
            DurationMinutes = 480,
            BreakStartMinutes = start + 240,
            BreakDurationMinutes = 30,
            TimeZoneId = "Europe/Moscow"
        }).ToList();

    private static Task<HttpResponseMessage> SaveScheduleAsync(
        HttpClient client, IReadOnlyList<ShiftScheduleItemDto> shifts) =>
        client.PutAsJsonAsync("/api/shifts", new SaveShiftsRequestDto { Shifts = shifts.ToList() });

    private static Task<HttpResponseMessage> PutDefaultsAsync(
        HttpClient client, params (int Position, string? ShiftLeadId)[] items) =>
        client.PutAsJsonAsync("/api/shifts/default-leads", new
        {
            items = items.Select(i => new { position = i.Position, shiftLeadId = i.ShiftLeadId }).ToArray()
        });

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
            await ClearDefaultsIfTableExistsAsync();
            var restore = original!.Select(s => new ShiftScheduleItemDto
            {
                StartMinutes = s.StartMinutes,
                DurationMinutes = s.DurationMinutes,
                BreakStartMinutes = s.BreakStartMinutes,
                BreakDurationMinutes = s.BreakDurationMinutes,
                TimeZoneId = s.TimeZoneId
            }).ToList();
            var response = await SaveScheduleAsync(client, restore);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    private static DateOnly NewFutureDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Interlocked.Increment(ref _dateOffset));

    private async Task SeedOperationalDayAsync(DateOnly date, string leadId, string operatorId)
    {
        await DeleteDayAsync(date);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var window = await CurrentWindowAsync(date, 1);
        var item = new ShiftPlanItem
        {
            Position = 1,
            StartAtUtc = window.StartAtUtc,
            EndAtUtc = window.EndAtUtc,
            TimeZoneId = window.TimeZoneId
        };
        item.OperatorAssignments.Add(new ShiftImmOperatorAssignment
        {
            ShiftPlanItem = item,
            ImmId = _factory.TestImmId,
            OperatorId = operatorId,
            Slot = 1,
            AssignedAt = window.StartAtUtc,
            UnassignedAt = window.EndAtUtc,
            CreatedById = leadId
        });
        item.Closure = new ShiftClosure
        {
            ShiftPlanItem = item,
            State = ShiftClosureState.Draft,
            CreatedById = leadId
        };
        db.ShiftPlanDays.Add(new ShiftPlanDay { ShiftDate = date, Items = [item] });
        await db.SaveChangesAsync();
    }

    private async Task SeedEmptyDayAsync(DateOnly date)
    {
        await DeleteDayAsync(date);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var window = await CurrentWindowAsync(date, 1);
        db.ShiftPlanDays.Add(new ShiftPlanDay
        {
            ShiftDate = date,
            Items =
            [
                new ShiftPlanItem
                {
                    Position = 1,
                    StartAtUtc = window.StartAtUtc,
                    EndAtUtc = window.EndAtUtc,
                    TimeZoneId = window.TimeZoneId
                }
            ]
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedDraftOnSecondPositionAsync(DateOnly date, string leadId)
    {
        await DeleteDayAsync(date);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var first = await CurrentWindowAsync(date, 1);
        var second = await CurrentWindowAsync(date, 2);
        var secondItem = new ShiftPlanItem
        {
            Position = 2,
            StartAtUtc = second.StartAtUtc,
            EndAtUtc = second.EndAtUtc,
            TimeZoneId = second.TimeZoneId,
            Closure = new ShiftClosure { State = ShiftClosureState.Draft, CreatedById = leadId }
        };
        db.ShiftPlanDays.Add(new ShiftPlanDay
        {
            ShiftDate = date,
            Items =
            [
                new ShiftPlanItem
                {
                    Position = 1,
                    StartAtUtc = first.StartAtUtc,
                    EndAtUtc = first.EndAtUtc,
                    TimeZoneId = first.TimeZoneId
                },
                secondItem
            ]
        });
        await db.SaveChangesAsync();
    }

    private async Task<ShiftWindow> CurrentWindowAsync(DateOnly date, int position)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var shifts = await db.Shifts.AsNoTracking().ToListAsync();
        return ShiftWindowCalculator.Build(date, shifts).Single(w => w.Position == position);
    }

    private async Task<User?> FirstActiveManagerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<List<string>> DeactivateActiveManagersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var ids = await db.Users.Where(u => u.IsActive && u.Role == UserRole.Manager).Select(u => u.Id).ToListAsync();
        await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
        return ids;
    }

    private async Task RestoreManagersAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0) return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, true));
    }

    private async Task<IReadOnlyList<DefaultRow>> DefaultRowsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"Position\", \"ShiftLeadId\" FROM \"ShiftDefaultLeads\" ORDER BY \"Position\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<DefaultRow>();
        while (await reader.ReadAsync()) rows.Add(new DefaultRow(reader.GetInt32(0), reader.GetString(1)));
        return rows;
    }

    private async Task ClearDefaultsIfTableExistsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public."ShiftDefaultLeads"') IS NOT NULL;
            """, connection);
        if ((bool)(await command.ExecuteScalarAsync())!)
        {
            command.CommandText = "DELETE FROM \"ShiftDefaultLeads\"";
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<ItemSnapshot> ItemSnapshotAsync(DateOnly date, int position)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var item = await db.ShiftPlanItems.AsNoTracking()
            .Include(i => i.OperatorAssignments)
            .Include(i => i.Closure)
            .SingleOrDefaultAsync(i => i.Day.ShiftDate == date && i.Position == position);
        item.Should().NotBeNull($"позиция {position} даты {date:yyyy-MM-dd} должна сохраниться");
        return new ItemSnapshot(
            item!.Id,
            item.Position,
            item.OperatorAssignments.SingleOrDefault()?.Id,
            item.Closure?.Id,
            item.Closure?.State);
    }

    private async Task<int> OperationalDayCountAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.ShiftPlanDays.CountAsync(d => d.ShiftDate == date);
    }

    private async Task DeleteDayAsync(DateOnly date)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        await db.ShiftPlanDays.Where(d => d.ShiftDate == date).ExecuteDeleteAsync();
    }

    private async Task<IReadOnlyList<string>> ScheduleKeysAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        return await db.Shifts.AsNoTracking().OrderBy(s => s.StartMinutes)
            .Select(s => $"{s.StartMinutes}:{s.DurationMinutes}:{s.BreakStartMinutes}:{s.BreakDurationMinutes}:{s.TimeZoneId}")
            .ToListAsync();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        string connectionString;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            connectionString = db.Database.GetDbConnection().ConnectionString;
        }
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<JsonElement> RequiredJsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions));
    }

    private static void AssertSettingsBody(JsonElement body, int expectedCount)
    {
        body.GetProperty("items").EnumerateArray().Should().HaveCount(expectedCount);
        body.TryGetProperty("fallbackManager", out _).Should().BeTrue();
    }
}
