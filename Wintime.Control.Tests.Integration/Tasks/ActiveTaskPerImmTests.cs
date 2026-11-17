using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class ActiveTaskPerImmTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public ActiveTaskPerImmTests(IntegrationTestFactory factory) => _factory = factory;

    [Theory]
    [InlineData(TaskStatus.Setup, TaskStatus.Setup)]
    [InlineData(TaskStatus.Setup, TaskStatus.InProgress)]
    [InlineData(TaskStatus.InProgress, TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress, TaskStatus.InProgress)]
    public async Task Database_RejectsSecondExecutingTaskOnSameImm(
        TaskStatus firstStatus, TaskStatus secondStatus)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(immId, firstStatus);

        var act = () => SeedTaskAsync(immId, secondStatus);

        var assertion = await act.Should().ThrowAsync<DbUpdateException>();
        assertion.WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task SeedTaskAsync(Guid immId, TaskStatus status)
    {
        var moldId = await _factory.CreateFreshMoldAsync();
        var now = DateTime.UtcNow;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = immId,
            MoldId = moldId,
            ProductId = _factory.TestProductId,
            PersonnelId = _factory.TestAdjusterId,
            PlanQuantity = 100,
            Cavities = 1,
            PlannedFullCycleSeconds = 60,
            PlannedDate = DateOnly.FromDateTime(now),
            Status = status,
            SetupStartedAt = status is TaskStatus.Setup or TaskStatus.InProgress ? now : null,
            StartedAt = status == TaskStatus.InProgress ? now : null
        });
        await db.SaveChangesAsync();
    }
}
