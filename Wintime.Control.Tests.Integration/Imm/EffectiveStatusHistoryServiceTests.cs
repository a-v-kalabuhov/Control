using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusHistoryServiceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusHistoryServiceTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GatherAsync_ReturnsInputsPerImm_AndEmptyForImmWithoutData()
    {
        var from = new DateTime(2026, 6, 25, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var withData = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var empty    = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Imms.AddRange(withData, empty);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        db.ImmStatusHistory.Add(new ImmStatusHistory
            { ImmId = withData.Id, Status = ImmStatus.Auto, ChangedAt = from, EndedAt = null });
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = withData.Id, MoldId = mold.Id, PlanQuantity = 100,
            Status = TaskStatus.InProgress, SetupStartedAt = from, StartedAt = from.AddMinutes(10)
        });
        db.Events.Add(new Event
            { ImmId = withData.Id, EventType = EventType.Downtime, StartTime = from.AddMinutes(40), EndTime = null });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IEffectiveStatusHistoryService>();
        var result = await service.GatherAsync(new[] { withData.Id, empty.Id }, from, to, effectiveTo: to);

        result.Should().ContainKeys(withData.Id, empty.Id);

        var a = result[withData.Id];
        a.Raw.Should().ContainSingle().Which.End.Should().Be(to);          // открытый статус обрезан effectiveTo
        a.Tasks.Should().HaveCount(2);                                      // наладка + работа
        a.Downtimes.Should().ContainSingle().Which.End.Should().Be(to);

        var b = result[empty.Id];
        b.Raw.Should().BeEmpty();
        b.Tasks.Should().BeEmpty();
        b.Downtimes.Should().BeEmpty();
    }

    [Fact]
    public async Task GatherAsync_ExcludesTasksEndedBeforeWindow_KeepsStillOpenEarlierTask()
    {
        var from = new DateTime(2026, 6, 25, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var imm = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Imms.Add(imm);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        // Завершено до окна — не должно попасть в результат.
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100, Status = TaskStatus.Completed,
            SetupStartedAt = from.AddDays(-3), StartedAt = from.AddDays(-3).AddMinutes(5),
            CompletedAt = from.AddDays(-2)
        });
        // Начато раньше и ещё открыто — пересекает окно.
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100, Status = TaskStatus.InProgress,
            SetupStartedAt = from.AddDays(-1), StartedAt = from.AddDays(-1).AddMinutes(5)
        });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IEffectiveStatusHistoryService>();
        var result = await service.GatherAsync(new[] { imm.Id }, from, to, effectiveTo: to);

        result[imm.Id].Tasks.Should().HaveCount(2);                                   // наладка + работа открытого задания
        result[imm.Id].Tasks.Should().OnlyContain(t => t.Start >= from.AddDays(-1));  // завершённого до окна нет
    }
}
