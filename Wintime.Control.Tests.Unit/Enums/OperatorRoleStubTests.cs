using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Shared.Constants;
using Xunit;

namespace Wintime.Control.Tests.Unit.Enums;

/// <summary>
/// ARCH-01: заглушка роли Operator под РОСОМС (ROS-03).
/// Роль должна существовать в enum и строковых константах, но её числовое
/// значение фиксируется (= 5), т.к. UserRole хранится в БД как int.
/// </summary>
public class OperatorRoleStubTests
{
    [Fact]
    public void Operator_HasStableNumericValue()
    {
        // Значение зафиксировано: перенумерация сломала бы уже сохранённые роли в БД.
        ((int)UserRole.Operator).Should().Be(5);
    }

    [Fact]
    public void Operator_EnumNameMatchesConstant()
    {
        UserRole.Operator.ToString().Should().Be(Roles.Operator);
    }

    [Fact]
    public void Operator_IsPresentInRolesAll()
    {
        Roles.All.Should().Contain(Roles.Operator);
    }
}
