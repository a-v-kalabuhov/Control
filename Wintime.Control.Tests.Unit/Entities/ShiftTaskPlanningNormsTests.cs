using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPlanningNormsTests
{
    private static ShiftTask NewTask(EntityTaskStatus status) => new()
    {
        Status = status, PlanQuantity = 10,
        PlannedSetupMinutes = 30, ReleaseBufferFixedMinutes = 10, ReleaseBufferPercent = 10
    };

    [Theory]
    [InlineData(EntityTaskStatus.Planned)]
    [InlineData(EntityTaskStatus.Issued)]
    public void SetSetupNorm_BeforeSetup_Changes(EntityTaskStatus status)
    {
        var task = NewTask(status);

        task.SetSetupNorm(90);

        task.PlannedSetupMinutes.Should().Be(90);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetSetupNorm_AfterSetupStarted_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetSetupNorm(90);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки можно изменить только до начала наладки");
        task.PlannedSetupMinutes.Should().Be(30);
    }

    [Fact]
    public void SetSetupNorm_Unchanged_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.Closed);

        var act = () => task.SetSetupNorm(30);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetSetupNorm_OutOfRange_Throws_EvenIfStatusAllows()
    {
        var task = NewTask(EntityTaskStatus.Planned);

        var act = () => task.SetSetupNorm(1441);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(EntityTaskStatus.Planned)]
    [InlineData(EntityTaskStatus.Issued)]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    public void SetReleaseBuffer_BeforeCompletion_Changes(EntityTaskStatus status)
    {
        var task = NewTask(status);

        task.SetReleaseBuffer(45, 20);

        task.ReleaseBufferFixedMinutes.Should().Be(45);
        task.ReleaseBufferPercent.Should().Be(20);
    }

    [Theory]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void SetReleaseBuffer_AfterCompletion_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetReleaseBuffer(45, 20);

        act.Should().Throw<DomainException>().WithMessage("Буфер выпуска можно изменить только до завершения выпуска");
    }

    [Fact]
    public void SetReleaseBuffer_Unchanged_InAnyStatus_NoOp()
    {
        var task = NewTask(EntityTaskStatus.Closed);

        var act = () => task.SetReleaseBuffer(10, 10);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetReleaseBuffer_PercentOutOfRange_Throws()
    {
        var task = NewTask(EntityTaskStatus.Planned);

        var act = () => task.SetReleaseBuffer(10, 101);

        act.Should().Throw<DomainException>().WithMessage("Процент буфера должен быть от 0 до 100");
    }
}
