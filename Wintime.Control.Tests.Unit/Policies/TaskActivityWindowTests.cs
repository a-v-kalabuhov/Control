using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskActivityWindowTests
{
    private static readonly DateTime T0 = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = T0.AddDays(3);

    private static ShiftTask Task(DateTime? setup, DateTime? started, DateTime? completed, DateTime? closed,
        TaskStatus status = TaskStatus.InProgress) => new()
    {
        Status = status, SetupStartedAt = setup, StartedAt = started, CompletedAt = completed, ClosedAt = closed
    };

    [Fact]
    public void Activity_NoSetupStarted_IsNull()
    {
        TaskActivityWindow.Activity(Task(null, null, null, null, TaskStatus.Issued), Now).Should().BeNull();
    }

    [Fact]
    public void Activity_Open_EndsAtNow()
    {
        TaskActivityWindow.Activity(Task(T0, T0.AddHours(1), null, null), Now)
            .Should().Be(new Interval(T0, Now));
    }

    [Fact]
    public void Activity_Completed_EndsAtCompletedAt()
    {
        TaskActivityWindow.Activity(Task(T0, T0.AddHours(1), T0.AddDays(2), T0.AddDays(2).AddHours(1)), Now)
            .Should().Be(new Interval(T0, T0.AddDays(2)));
    }

    [Fact]
    public void Activity_ClosedWithoutCompletion_EndsAtClosedAt()
    {
        TaskActivityWindow.Activity(Task(T0, null, null, T0.AddHours(5), TaskStatus.Closed), Now)
            .Should().Be(new Interval(T0, T0.AddHours(5)));
    }

    [Fact]
    public void Release_NoStartedAt_IsNull()
    {
        TaskActivityWindow.Release(Task(T0, null, null, T0.AddHours(5), TaskStatus.Closed), Now).Should().BeNull();
    }

    [Fact]
    public void Release_StartsAtStartedAt()
    {
        TaskActivityWindow.Release(Task(T0, T0.AddHours(1), T0.AddDays(2), null), Now)
            .Should().Be(new Interval(T0.AddHours(1), T0.AddDays(2)));
    }

    [Fact]
    public void Activity_EndBeforeStart_CollapsesToEmpty()
    {
        TaskActivityWindow.Activity(Task(T0, null, null, T0.AddMinutes(-1), TaskStatus.Closed), Now)
            .Should().Be(new Interval(T0, T0));
    }

    [Theory]
    // окно задания [T0+0h, T0+48h)
    [InlineData(-10, -1, false)] // период целиком до задания
    [InlineData(-10, 0, false)]  // период заканчивается ровно в начале — [from, to) не пересекается
    [InlineData(-1, 1, true)]    // захватывает начало
    [InlineData(10, 20, true)]   // внутри
    [InlineData(47, 60, true)]   // захватывает конец
    [InlineData(48, 60, false)]  // начинается ровно в конце
    public void OverlapsPeriod_ClosedTask(int fromH, int toH, bool expected)
    {
        var task = Task(T0, T0.AddHours(1), T0.AddHours(48), null, TaskStatus.Completed);
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddHours(fromH), T0.AddHours(toH)).Compile();
        predicate(task).Should().Be(expected);
    }

    [Fact]
    public void OverlapsPeriod_OpenTask_MatchesAnyPeriodAfterStart()
    {
        var task = Task(T0, T0.AddHours(1), null, null);
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddDays(5), T0.AddDays(6)).Compile();
        predicate(task).Should().BeTrue();
    }

    [Fact]
    public void OverlapsPeriod_NotStarted_False()
    {
        var predicate = TaskActivityWindow.OverlapsPeriod(T0.AddDays(-1), T0.AddDays(1)).Compile();
        predicate(Task(null, null, null, null, TaskStatus.Issued)).Should().BeFalse();
    }
}
