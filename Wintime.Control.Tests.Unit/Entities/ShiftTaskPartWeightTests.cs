using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPartWeightTests
{
    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void SetPartWeight_IsAllowedInAnyStatus(TaskStatus status)
    {
        var task = new ShiftTask { Status = status, PartWeightGrams = 10m };

        task.SetPartWeight(12.5m);

        task.PartWeightGrams.Should().Be(12.5m);
    }

    [Fact]
    public void SetPartWeight_RejectsNegativeWeight()
    {
        var task = new ShiftTask();

        var act = () => task.SetPartWeight(-0.01m);

        act.Should().Throw<DomainException>().WithMessage("*неотрицательным*");
    }
}
