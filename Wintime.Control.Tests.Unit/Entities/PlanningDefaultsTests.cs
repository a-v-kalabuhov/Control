using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Unit.Entities;

public class PlanningDefaultsTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_IsSingletonWithZeros()
    {
        var d = new PlanningDefaults();

        d.Id.Should().Be(PlanningDefaults.SingletonId);
        d.SetupMinutes.Should().Be(0);
        d.ReleaseBufferFixedMinutes.Should().Be(0);
        d.ReleaseBufferPercent.Should().Be(0);
    }

    [Fact]
    public void Update_Valid_SetsValuesAndAudit()
    {
        var d = new PlanningDefaults();

        d.Update(90, 30, 10, "user-1", Now);

        d.SetupMinutes.Should().Be(90);
        d.ReleaseBufferFixedMinutes.Should().Be(30);
        d.ReleaseBufferPercent.Should().Be(10);
        d.UpdatedAt.Should().Be(Now);
        d.UpdatedById.Should().Be("user-1");
    }

    [Fact]
    public void Update_OutOfRange_ThrowsAndKeepsValues()
    {
        var d = new PlanningDefaults();

        var act = () => d.Update(1441, 0, 0, "user-1", Now);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
        d.SetupMinutes.Should().Be(0);
        d.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Conflict_threshold_defaults_to_20_minutes()
    {
        new PlanningDefaults().ConflictThresholdMinutes.Should().Be(20);
    }

    [Fact]
    public void Update_sets_conflict_threshold_when_given()
    {
        var d = new PlanningDefaults();
        d.Update(0, 0, 0, "u", DateTime.UtcNow, conflictThresholdMinutes: 45);
        d.ConflictThresholdMinutes.Should().Be(45);
    }

    [Fact]
    public void Update_keeps_conflict_threshold_when_omitted()
    {
        var d = new PlanningDefaults();
        d.Update(10, 0, 0, "u", DateTime.UtcNow);
        d.ConflictThresholdMinutes.Should().Be(20);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Update_rejects_conflict_threshold_out_of_range(int minutes)
    {
        var act = () => new PlanningDefaults().Update(0, 0, 0, "u", DateTime.UtcNow, conflictThresholdMinutes: minutes);
        act.Should().Throw<DomainException>().WithMessage("*Порог конфликта*");
    }
}
