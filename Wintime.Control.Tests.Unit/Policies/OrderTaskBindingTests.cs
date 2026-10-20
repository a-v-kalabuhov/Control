using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class OrderTaskBindingTests
{
    private static Order ActiveOrder(Guid productId) =>
        new() { Status = OrderStatus.Active, ProductId = productId, Quantity = 100, Number = "O-1" };

    [Fact]
    public void EnsureCanBind_MatchingActive_DoesNotThrow()
    {
        var p = Guid.NewGuid();
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(p), p);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanBind_ProductMismatch_Throws()
    {
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), Guid.NewGuid());
        act.Should().Throw<DomainException>().WithMessage("Изделие задания не совпадает с изделием заказа");
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void EnsureCanBind_NonActiveOrder_Throws(OrderStatus status)
    {
        var p = Guid.NewGuid();
        var order = new Order { Status = status, ProductId = p, Quantity = 100, Number = "O-1" };
        var act = () => OrderTaskBinding.EnsureCanBind(order, p);
        act.Should().Throw<DomainException>();
    }
}
