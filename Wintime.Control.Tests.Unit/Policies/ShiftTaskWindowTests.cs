using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftTaskWindowTests
{
    private static readonly DateTime ShiftStart = Utc("2026-11-08T08:00:00Z");
    private static readonly DateTime ShiftEnd = Utc("2026-11-08T16:00:00Z");
    private static readonly DateTime Now = Utc("2026-11-08T20:00:00Z");

    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    public void Belongs_PlannedAndIssued_UsePlannedStartInsideHalfOpenShift(TaskStatus status)
    {
        var task = Task(status);

        Belongs(task, ShiftStart).Should().BeTrue("начало смены входит в окно");
        Belongs(task, ShiftStart.AddTicks(-1)).Should().BeFalse();
        Belongs(task, ShiftEnd.AddTicks(-1)).Should().BeTrue();
        Belongs(task, ShiftEnd).Should().BeFalse("окно смены не включает EndAtUtc");
        Belongs(task, ShiftEnd.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void Belongs_PlannedTaskWithActivityOutsideWindow_StillUsesOnlyPlannedStart()
    {
        var task = Task(TaskStatus.Issued, setupStartedAt: ShiftStart.AddHours(1));

        Belongs(task, ShiftEnd.AddDays(1)).Should().BeFalse();
    }

    [Theory]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void Belongs_ActivityStatuses_UseActivityOverlap(TaskStatus status)
    {
        var task = Task(status,
            plannedStartAt: ShiftEnd.AddDays(2),
            setupStartedAt: ShiftStart.AddHours(1),
            completedAt: status == TaskStatus.Completed ? ShiftStart.AddHours(2) : null,
            closedAt: status == TaskStatus.Closed ? ShiftStart.AddHours(2) : null);

        Belongs(task, ShiftEnd.AddDays(2)).Should().BeTrue(
            "плановое начало вне смены не отменяет пересечение фактической активности");
    }

    [Fact]
    public void Belongs_CompletedInsideShift_IsIncludedAndCompletedBeforeShift_IsExcluded()
    {
        var inside = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(1), completedAt: ShiftStart.AddHours(2));
        var before = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(-3), completedAt: ShiftStart.AddHours(-1));

        Belongs(inside, ShiftEnd.AddDays(1)).Should().BeTrue();
        Belongs(before, ShiftStart).Should().BeFalse();
    }

    [Fact]
    public void Belongs_ActivityEndingExactlyAtShiftStart_IsExcluded()
    {
        var task = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(-2), completedAt: ShiftStart);

        Belongs(task, ShiftStart.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Belongs_MultiShiftTask_IsIncludedInEveryIntersectedShift()
    {
        var task = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(-1), completedAt: ShiftEnd.AddHours(20));
        var nextStart = ShiftEnd.AddHours(8);
        var nextEnd = nextStart.AddHours(8);

        ShiftTaskWindow.Belongs(task, ShiftEnd.AddDays(1), ShiftStart, ShiftEnd, Now).Should().BeTrue();
        ShiftTaskWindow.Belongs(task, ShiftEnd.AddDays(1), nextStart, nextEnd, Now).Should().BeTrue();
    }

    [Fact]
    public void Belongs_OpenActivity_DoesNotReachFutureShiftBeyondNow()
    {
        var task = Task(TaskStatus.InProgress, setupStartedAt: ShiftStart.AddHours(1));
        var futureStart = Now.AddHours(1);

        ShiftTaskWindow.Belongs(task, ShiftEnd.AddDays(1), futureStart, futureStart.AddHours(8), Now)
            .Should().BeFalse("открытое окно активности заканчивается текущим моментом");
    }

    [Fact]
    public void Belongs_ActivityWithoutSetupStart_IsExcluded()
    {
        var task = Task(TaskStatus.InProgress);

        Belongs(task, ShiftStart).Should().BeFalse();
    }

    [Fact]
    public void ActivityOverlaps_UsesTheSameHalfOpenBoundariesAsActivityWindow()
    {
        var endingAtStart = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(-2), completedAt: ShiftStart);
        var crossingStart = Task(TaskStatus.Completed,
            setupStartedAt: ShiftStart.AddHours(-2), completedAt: ShiftStart.AddTicks(1));
        var startingAtEnd = Task(TaskStatus.Setup, setupStartedAt: ShiftEnd);

        new[] { endingAtStart, crossingStart, startingAtEnd }.AsQueryable()
            .Where(ShiftTaskWindow.ActivityOverlaps(ShiftStart, ShiftEnd))
            .Should().ContainSingle().Which.Should().BeSameAs(crossingStart);
    }

    private static bool Belongs(ShiftTask task, DateTime plannedStartAt) =>
        ShiftTaskWindow.Belongs(task, plannedStartAt, ShiftStart, ShiftEnd, Now);

    private static ShiftTask Task(
        TaskStatus status,
        DateTime? plannedStartAt = null,
        DateTime? setupStartedAt = null,
        DateTime? completedAt = null,
        DateTime? closedAt = null) => new()
    {
        Status = status,
        PlannedDate = DateOnly.FromDateTime(plannedStartAt ?? ShiftStart),
        SetupStartedAt = setupStartedAt,
        CompletedAt = completedAt,
        ClosedAt = closedAt
    };

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
