using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskSetProductTests
{
    [Fact]
    public void SetProduct_Planned_WithoutOrder_Changes()
    {
        var t = new ShiftTask { Status = TaskStatus.Planned, ProductId = Guid.NewGuid() };
        var p = Guid.NewGuid();
        t.SetProduct(p);
        t.ProductId.Should().Be(p);
    }

    [Fact]
    public void SetProduct_SameValue_IsNoOp_InAnyStatus()
    {
        var p = Guid.NewGuid();
        var t = new ShiftTask { Status = TaskStatus.InProgress, ProductId = p, OrderId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(p)).Should().NotThrow();
    }

    [Fact]
    public void SetProduct_Issued_Throws()
    {
        var t = new ShiftTask { Status = TaskStatus.Issued, ProductId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(Guid.NewGuid())).Should().Throw<DomainException>();
    }

    [Fact]
    public void SetProduct_WithOrder_Throws()
    {
        var t = new ShiftTask { Status = TaskStatus.Planned, ProductId = Guid.NewGuid(), OrderId = Guid.NewGuid() };
        FluentActions.Invoking(() => t.SetProduct(Guid.NewGuid()))
            .Should().Throw<DomainException>().WithMessage("Изделие задания с заказом задаёт заказ");
    }
}
