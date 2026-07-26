using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class OrderTaskBindingTests
{
    private static Order ActiveOrder(Guid productTypeId) =>
        new() { Status = OrderStatus.Active, ProductTypeId = productTypeId, Quantity = 100, Number = "O-1" };

    [Fact]
    public void EnsureCanBind_MatchingActive_DoesNotThrow()
    {
        var pt = Guid.NewGuid();
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(pt), pt);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanBind_ProductTypeMismatch_Throws()
    {
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), Guid.NewGuid());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EnsureCanBind_MoldWithoutType_Throws()
    {
        var act = () => OrderTaskBinding.EnsureCanBind(ActiveOrder(Guid.NewGuid()), null);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void EnsureCanBind_NonActiveOrder_Throws(OrderStatus status)
    {
        var pt = Guid.NewGuid();
        var order = new Order { Status = status, ProductTypeId = pt, Quantity = 100, Number = "O-1" };
        var act = () => OrderTaskBinding.EnsureCanBind(order, pt);
        act.Should().Throw<DomainException>();
    }
}
