using FluentAssertions;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskPlanCalculatorTests
{
    private static readonly DateTime Start = new(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Calculate_FullData_SumsSetupReleaseBuffer()
    {
        // 100 шт / 4 гнезда = 25 смыканий × 60 с = 1500 с; буфер 10 мин + 10 % от 1500 = 600 + 150
        var plan = TaskPlanCalculator.Calculate(Start, setupMinutes: 30, planQuantity: 100, cavities: 4,
            fullCycleSeconds: 60, bufferFixedMinutes: 10, bufferPercent: 10);

        plan.SetupSeconds.Should().Be(1800);
        plan.ReleaseSeconds.Should().Be(1500);
        plan.BufferSeconds.Should().Be(750);
        plan.PlannedEndAt.Should().Be(Start.AddSeconds(1800 + 1500 + 750));
        plan.PlannedEndAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_PartialLastShot_RoundsShotsUp()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, planQuantity: 101, cavities: 4,
            fullCycleSeconds: 60, bufferFixedMinutes: 0, bufferPercent: 0);

        plan.ReleaseSeconds.Should().Be(26 * 60);
    }

    [Fact]
    public void Calculate_PercentPart_RoundsUpToSecond()
    {
        // 10 % от 33 с = 3.3 → 4
        var plan = TaskPlanCalculator.Calculate(Start, 0, planQuantity: 1, cavities: 1,
            fullCycleSeconds: 33, bufferFixedMinutes: 0, bufferPercent: 10);

        plan.BufferSeconds.Should().Be(4);
    }

    [Fact]
    public void Calculate_PercentBase_ExcludesSetup()
    {
        var plan = TaskPlanCalculator.Calculate(Start, setupMinutes: 600, planQuantity: 1, cavities: 1,
            fullCycleSeconds: 60, bufferFixedMinutes: 0, bufferPercent: 100);

        plan.BufferSeconds.Should().Be(60);
    }

    [Fact]
    public void Calculate_NoFullCycle_OnlySetupKnown()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 45, 100, 4, fullCycleSeconds: null, 10, 10);

        plan.SetupSeconds.Should().Be(2700);
        plan.ReleaseSeconds.Should().BeNull();
        plan.BufferSeconds.Should().BeNull();
        plan.PlannedEndAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Calculate_NoCavities_OnlySetupKnown(int cavities)
    {
        var plan = TaskPlanCalculator.Calculate(Start, 45, 100, cavities, 60, 10, 10);

        plan.ReleaseSeconds.Should().BeNull();
        plan.BufferSeconds.Should().BeNull();
        plan.PlannedEndAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroNorms_EndIsStartPlusRelease()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, 10, 1, 60, 0, 0);

        plan.BufferSeconds.Should().Be(0);
        plan.PlannedEndAt.Should().Be(Start.AddSeconds(600));
    }

    [Fact]
    public void Calculate_LargeValues_NoOverflow()
    {
        var plan = TaskPlanCalculator.Calculate(Start, 0, 1_000_000, 1, 86_400, 0, 100);

        plan.ReleaseSeconds.Should().Be(86_400_000_000L);
        plan.BufferSeconds.Should().Be(86_400_000_000L);
        plan.PlannedEndAt.Should().Be(Start.AddSeconds(2 * 86_400_000_000L));
    }

    [Fact]
    public void Calculate_EndBeyondDateTimeMax_EndIsNullButDurationsKnown()
    {
        var act = () => TaskPlanCalculator.Calculate(Start, 0, int.MaxValue, 1, 86_400, 0, 0);

        var plan = act.Should().NotThrow().Subject;
        plan.ReleaseSeconds.Should().Be(int.MaxValue * 86_400L);
        plan.PlannedEndAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ArithmeticOverflow_ReturnsNotComputable()
    {
        var act = () => TaskPlanCalculator.Calculate(Start, 0, int.MaxValue, 1, int.MaxValue, 0, 100);

        var plan = act.Should().NotThrow().Subject;
        plan.ReleaseSeconds.Should().BeNull();
        plan.BufferSeconds.Should().BeNull();
        plan.PlannedEndAt.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void EnsureSetupMinutes_OutOfRange_Throws(int minutes)
    {
        var act = () => PlanningNormLimits.EnsureSetupMinutes(minutes);

        act.Should().Throw<DomainException>().WithMessage("Норматив наладки должен быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void EnsureBuffer_FixedOutOfRange_Throws(int fixedMinutes)
    {
        var act = () => PlanningNormLimits.EnsureBuffer(fixedMinutes, 0);

        act.Should().Throw<DomainException>().WithMessage("Фиксированная часть буфера должна быть от 0 до 1440 минут");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void EnsureBuffer_PercentOutOfRange_Throws(int percent)
    {
        var act = () => PlanningNormLimits.EnsureBuffer(0, percent);

        act.Should().Throw<DomainException>().WithMessage("Процент буфера должен быть от 0 до 100");
    }

    [Fact]
    public void Ensure_Bounds_Allowed()
    {
        var act = () =>
        {
            PlanningNormLimits.EnsureSetupMinutes(0);
            PlanningNormLimits.EnsureSetupMinutes(1440);
            PlanningNormLimits.EnsureBuffer(1440, 100);
            PlanningNormLimits.EnsureBuffer(0, 0);
        };

        act.Should().NotThrow();
    }
}
