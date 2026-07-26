using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Imm;

/// <summary>
/// PZP-06: карточка ТПА в режиме «Работа без задания» показывает счётчик сирот-циклов
/// текущего открытого эпизода UnplannedRun (а не 0).
/// </summary>
[Collection("Integration")]
public class ImmListUnplannedCycleCountTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ImmListUnplannedCycleCountTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<ImmDto> GetImmFromListAsync(Guid immId)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var list = await client.GetFromJsonAsync<List<ImmDto>>("/api/imm");
        return list!.Single(i => i.Id == immId);
    }

    [Fact]
    public async Task Card_shows_orphan_cycle_count_of_open_unplanned_run()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = start }); // открыт (ClosedAt=null)
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddSeconds(60), EndTime = start.AddSeconds(120), DurationSeconds = 60, IsSuccessful = true });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddSeconds(120), EndTime = start.AddSeconds(180), DurationSeconds = 60, IsSuccessful = false });
            await db.SaveChangesAsync();
        }

        var dto = await GetImmFromListAsync(immId);

        dto.CurrentTaskId.Should().BeNull();
        dto.CycleCount.Should().Be(3); // сироты считаются вне зависимости от годности
    }

    [Fact]
    public async Task Card_shows_zero_when_episode_is_closed()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(5) });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
        }

        var dto = await GetImmFromListAsync(immId);

        dto.CycleCount.Should().Be(0); // закрытый эпизод не подмешивается в карточку
    }

    [Fact]
    public async Task Card_ignores_orphan_cycles_before_episode_start()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-10);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = start });
            // цикл ДО начала эпизода — не считается
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddMinutes(-11), EndTime = start.AddMinutes(-10).AddSeconds(-1), DurationSeconds = 60, IsSuccessful = true });
            // цикл внутри эпизода — считается
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddSeconds(1), EndTime = start.AddSeconds(61), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
        }

        var dto = await GetImmFromListAsync(immId);

        dto.CycleCount.Should().Be(1);
    }
}
