using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class ShiftTaskCycleNormsTests
{
    private static ShiftTask NewTask() => new() { ImmId = Guid.NewGuid(), MoldId = Guid.NewGuid() };

    [Fact]
    public void Default_WorkMode_IsAuto()
    {
        NewTask().WorkMode.Should().Be(WorkMode.Auto);
    }

    [Fact]
    public void SetCycleNorms_ValidValues_AssignsAll()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.SemiAuto, 60, 25, requireFullCycle: true);

        task.WorkMode.Should().Be(WorkMode.SemiAuto);
        task.PlannedFullCycleSeconds.Should().Be(60);
        task.PlannedInjectionCycleSeconds.Should().Be(25);
    }

    [Fact]
    public void SetCycleNorms_NullFullCycle_WhenRequired_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, null, null, requireFullCycle: true);

        act.Should().Throw<DomainException>()
           .WithMessage("*полного цикла*");
    }

    [Fact]
    public void SetCycleNorms_NullFullCycle_WhenNotRequired_KeepsNull()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.Auto, null, null, requireFullCycle: false);

        task.PlannedFullCycleSeconds.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetCycleNorms_NonPositiveFullCycle_Throws(int seconds)
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, seconds, null, requireFullCycle: true);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SetCycleNorms_NonPositiveInjectionCycle_Throws(int seconds)
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, 60, seconds, requireFullCycle: true);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleGreaterThanFullCycle_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, 60, 61, requireFullCycle: true);

        act.Should().Throw<DomainException>()
           .WithMessage("*не может превышать*");
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleEqualToFullCycle_Allowed()
    {
        var task = NewTask();

        task.SetCycleNorms(WorkMode.Auto, 60, 60, requireFullCycle: true);

        task.PlannedInjectionCycleSeconds.Should().Be(60);
    }

    [Fact]
    public void SetCycleNorms_InjectionCycleWithoutFullCycle_OnLegacyTask_Throws()
    {
        var task = NewTask();

        var act = () => task.SetCycleNorms(WorkMode.Auto, null, 25, requireFullCycle: false);

        act.Should().Throw<DomainException>()
           .WithMessage("*без эталона полного цикла*");
    }
}
