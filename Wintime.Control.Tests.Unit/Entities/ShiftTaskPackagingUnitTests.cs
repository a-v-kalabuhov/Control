using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskPackagingUnitTests
{
    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void SetPackagingUnit_PositiveValue_WorksInEveryStatus(TaskStatus status)
    {
        var task = new ShiftTask { Status = status, PackagingUnit = 12 };

        task.SetPackagingUnit(24);

        task.PackagingUnit.Should().Be(24);
    }

    [Fact]
    public void SetPackagingUnit_Null_ClearsValue()
    {
        var task = new ShiftTask { PackagingUnit = 24 };

        task.SetPackagingUnit(null);

        task.PackagingUnit.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetPackagingUnit_NonPositiveValue_Throws(int value)
    {
        var task = new ShiftTask();

        var act = () => task.SetPackagingUnit(value);

        act.Should().Throw<DomainException>().WithMessage("Тарная единица должна быть больше нуля");
    }
}
