using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftClosurePlanCalculatorTests
{
    private static readonly DateTime ShiftStart = Utc("2026-11-18T08:00:00");
    private static readonly DateTime ShiftEnd = Utc("2026-11-18T10:00:00");

    [Fact]
    public void Plan_for_open_release_uses_full_shift_not_fact_end()
    {
        var task = StartedTask(planQuantity: 100, cavities: 2, cycleSeconds: 60,
            startedAt: Utc("2026-11-18T08:00:00"));
        var plan = ShiftClosurePlanCalculator.Calculate(task,
            Utc("2026-11-18T07:30:00"), Utc("2026-11-18T08:00:00"),
            Utc("2026-11-18T10:00:00"), previousOutput: 20);

        plan.Should().Be(80); // норматив за 2 часа = 100, ограничен остатком 80; ClosedAt не параметр
    }

    [Fact]
    public void Planned_release_starts_after_setup_norm()
    {
        var task = PlannedTask(planQuantity: 1000, cavities: 2, cycleSeconds: 60, setupMinutes: 30);

        Calculate(task, ShiftStart, previousOutput: 0).Should().Be(180);
    }

    [Fact]
    public void Started_release_counts_only_partial_shift_after_actual_start()
    {
        var task = StartedTask(planQuantity: 1000, cavities: 2, cycleSeconds: 60,
            startedAt: ShiftStart.AddMinutes(30));

        Calculate(task, ShiftStart.AddHours(-2), previousOutput: 0).Should().Be(180);
    }

    [Fact]
    public void Multi_shift_release_does_not_restart_at_shift_start()
    {
        var task = StartedTask(planQuantity: 1000, cavities: 2, cycleSeconds: 60,
            startedAt: ShiftStart.AddHours(-1));

        Calculate(task, ShiftStart.AddHours(-1), previousOutput: 0).Should().Be(240);
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void Finished_release_stops_at_actual_end(TaskStatus status)
    {
        var task = StartedTask(planQuantity: 1000, cavities: 2, cycleSeconds: 60,
            startedAt: ShiftStart);
        task.Status = status;
        if (status == TaskStatus.Completed)
            task.CompletedAt = ShiftStart.AddHours(1);
        else
            task.ClosedAt = ShiftStart.AddHours(1);

        Calculate(task, ShiftStart, previousOutput: 0).Should().Be(120);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(60, 0)]
    public void Not_calculable_norm_returns_zero(int? cycleSeconds, int cavities)
    {
        var planned = PlannedTask(planQuantity: 100, cavities, cycleSeconds, setupMinutes: 0);
        var started = StartedTask(planQuantity: 100, cavities, cycleSeconds, ShiftStart);

        Calculate(planned, ShiftStart, previousOutput: 0).Should().Be(0);
        Calculate(started, ShiftStart, previousOutput: 0).Should().Be(0);
    }

    [Theory]
    [InlineData(100, 20, 80)]
    [InlineData(100, 100, 0)]
    [InlineData(100, 120, 0)]
    [InlineData(0, 0, 0)]
    public void Remaining_at_start_is_never_negative(int planQuantity, int previousOutput, int expected)
    {
        ShiftClosurePlanCalculator.RemainingAtStart(planQuantity, previousOutput).Should().Be(expected);
    }

    [Fact]
    public void Adjacent_shift_plans_do_not_exceed_task_norm()
    {
        var task = StartedTask(planQuantity: 100, cavities: 2, cycleSeconds: 60, startedAt: ShiftStart);

        var first = ShiftClosurePlanCalculator.Calculate(task, ShiftStart, ShiftStart, ShiftEnd, 0);
        var second = ShiftClosurePlanCalculator.Calculate(
            task, ShiftStart, ShiftEnd, ShiftEnd.AddHours(2), previousOutput: 20);

        first.Should().Be(100);
        second.Should().Be(0);
        (first + second).Should().Be(100);
    }

    [Fact]
    public void Material_includes_runner_once_for_last_partial_shot()
    {
        ShiftClosurePlanCalculator.MaterialWeightGrams(5, 2, 10m, 4m)
            .Should().Be(62m); // 5*10 + ceil(5/2)*4
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(5, 0)]
    [InlineData(5, -2)]
    public void Material_for_nonpositive_quantity_or_cavities_is_zero(int quantity, int cavities)
    {
        ShiftClosurePlanCalculator.MaterialWeightGrams(quantity, cavities, 10m, 4m).Should().Be(0m);
    }

    private static int Calculate(ShiftTask task, DateTime plannedStartAtUtc, int previousOutput) =>
        ShiftClosurePlanCalculator.Calculate(
            task, plannedStartAtUtc, ShiftStart, ShiftEnd, previousOutput);

    private static ShiftTask PlannedTask(
        int planQuantity, int cavities, int? cycleSeconds, int setupMinutes) => new()
    {
        Status = TaskStatus.Planned,
        PlanQuantity = planQuantity,
        Cavities = cavities,
        PlannedFullCycleSeconds = cycleSeconds,
        PlannedSetupMinutes = setupMinutes,
        ReleaseBufferFixedMinutes = 30,
        ReleaseBufferPercent = 50
    };

    private static ShiftTask StartedTask(
        int planQuantity, int cavities, int? cycleSeconds, DateTime startedAt) => new()
    {
        Status = TaskStatus.InProgress,
        PlanQuantity = planQuantity,
        Cavities = cavities,
        PlannedFullCycleSeconds = cycleSeconds,
        SetupStartedAt = startedAt.AddMinutes(-30),
        StartedAt = startedAt
    };

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
