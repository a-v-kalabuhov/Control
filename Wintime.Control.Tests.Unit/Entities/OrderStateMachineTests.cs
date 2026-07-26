using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class OrderStateMachineTests
{
    private static Order NewOrder(OrderStatus status, int quantity = 100) =>
        new() { Status = status, Quantity = quantity, Number = "ORD-1" };

    [Fact]
    public void Complete_WhenEnoughGood_MovesToCompleted()
    {
        var order = NewOrder(OrderStatus.Active, quantity: 100);
        order.Complete(goodQuantity: 100);
        order.Status.Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public void Complete_WhenNotEnoughGood_Throws()
    {
        var order = NewOrder(OrderStatus.Active, quantity: 100);
        var act = () => order.Complete(goodQuantity: 99);
        act.Should().Throw<DomainException>();
        order.Status.Should().Be(OrderStatus.Active);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Complete_FromNonActive_Throws(OrderStatus status)
    {
        var order = NewOrder(status);
        var act = () => order.Complete(goodQuantity: 1000);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancel_FromActive_MovesToCancelled()
    {
        var order = NewOrder(OrderStatus.Active);
        order.Cancel();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Cancel_FromNonActive_Throws(OrderStatus status)
    {
        var order = NewOrder(status);
        var act = () => order.Cancel();
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Reopen_FromNonActive_MovesToActive(OrderStatus status)
    {
        var order = NewOrder(status);
        order.Reopen();
        order.Status.Should().Be(OrderStatus.Active);
    }

    [Fact]
    public void Reopen_FromActive_Throws()
    {
        var order = NewOrder(OrderStatus.Active);
        var act = () => order.Reopen();
        act.Should().Throw<DomainException>();
    }
}
