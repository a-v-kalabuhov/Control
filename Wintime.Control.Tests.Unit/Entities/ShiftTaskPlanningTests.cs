using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPlanningTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 10, 0, 0); // время завода
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static ShiftTask NewTask(EntityTaskStatus status) => new() { Status = status, PlanQuantity = 10 };

    [Fact]
    public void SetPlannedStart_Draft_SetsDateAndTime()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetPlannedStart(Today.AddDays(1), new TimeOnly(14, 0), Now);

        task.PlannedDate.Should().Be(Today.AddDays(1));
        task.PlannedSetupStartTime.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public void SetPlannedStart_Issued_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Issued);

        task.SetPlannedStart(Today, null, Now);

        task.PlannedDate.Should().Be(Today);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetPlannedStart_AfterSetupStarted_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetPlannedStart(Today.AddDays(1), null, Now);

        act.Should().Throw<DomainException>().WithMessage("Плановое начало можно изменить только до начала наладки");
    }

    [Fact]
    public void SetPlannedStart_UnchangedValue_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.InProgress);
        task.PlannedDate = Today.AddDays(-3);

        var act = () => task.SetPlannedStart(Today.AddDays(-3), null, Now);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetPlannedStart_PastDate_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetPlannedStart(Today.AddDays(-1), null, Now);

        act.Should().Throw<DomainException>().WithMessage("Плановая дата не может быть в прошлом");
    }

    [Fact]
    public void SetPlannedStart_TodayPastTime_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetPlannedStart(Today, new TimeOnly(9, 0), Now);

        act.Should().Throw<DomainException>().WithMessage("Время начала наладки не может быть в прошлом");
    }

    [Fact]
    public void SetPlannedStart_TodayTimeWithinTolerance_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetPlannedStart(Today, new TimeOnly(9, 56), Now);

        task.PlannedSetupStartTime.Should().Be(new TimeOnly(9, 56));
    }

    [Fact]
    public void SetPlannedStart_JustAfterMidnight_FutureTimeAllowed()
    {
        // Регрессия: допуск не должен «заворачиваться» через полночь.
        var task = NewTask(EntityTaskStatus.Draft);
        var now = new DateTime(2026, 10, 15, 0, 2, 0);

        task.SetPlannedStart(Today, new TimeOnly(8, 0), now);

        task.PlannedSetupStartTime.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public void SetPlannedStart_ClearTimeOnPastDate_Allowed()
    {
        var task = NewTask(EntityTaskStatus.Issued);
        task.PlannedDate = Today.AddDays(-1);
        task.PlannedSetupStartTime = new TimeOnly(8, 0);

        task.SetPlannedStart(Today.AddDays(-1), null, Now);

        task.PlannedSetupStartTime.Should().BeNull();
    }
}
