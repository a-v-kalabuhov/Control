using FluentAssertions;
using Wintime.Control.Core.Enums;
using Xunit;

namespace Wintime.Control.Tests.Unit.Enums;

/// <summary>
/// ARCH-02: заглушка статуса пресс-формы под РОСОМС (ROS-01).
/// Enum должен существовать; его числовые значения фиксируются,
/// т.к. MoldStatus хранится в БД как nullable int.
/// </summary>
public class MoldStatusStubTests
{
    [Theory]
    [InlineData(MoldStatus.InWork, 0)]
    [InlineData(MoldStatus.InRepair, 1)]
    [InlineData(MoldStatus.Modernization, 2)]
    [InlineData(MoldStatus.Maintenance, 3)]
    public void MoldStatus_HasStableNumericValues(MoldStatus status, int expected)
    {
        // Перенумерация сломала бы уже сохранённые значения в колонке Molds.MoldStatus.
        ((int)status).Should().Be(expected);
    }

    [Fact]
    public void MoldStatus_HasExactlyFourValues()
    {
        Enum.GetValues<MoldStatus>().Should().HaveCount(4);
    }
}
