using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftClosureCycleMetricsTests
{
    private static readonly DateTime ShiftStart = Utc("2026-11-18T08:00:00");
    private static readonly DateTime FactEnd = Utc("2026-11-18T10:00:00");

    [Fact]
    public void Metrics_use_end_time_and_exclude_open_unsuccessful_and_shift_end()
    {
        var taskId = Guid.NewGuid();
        var cycles = new[]
        {
            ClosedCycle(taskId, "2026-11-18T07:59:59", cavities: 2, duration: 20),
            ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 3, duration: 30),
            ClosedCycle(taskId, "2026-11-18T09:59:59", cavities: 4, duration: 40),
            ClosedCycle(taskId, "2026-11-18T10:00:00", cavities: 8, duration: 50),
            OpenCycle(taskId, cavities: 16)
        };

        var result = ShiftClosureCycleMetrics.Calculate(cycles, taskId,
            Utc("2026-11-18T08:00:00"), Utc("2026-11-18T10:00:00"));

        result.OutputQuantity.Should().Be(7);
        result.AverageFullCycleSeconds.Should().Be(35);
    }

    [Fact]
    public void Metrics_exclude_unsuccessful_cycles()
    {
        var taskId = Guid.NewGuid();
        var cycles = new[]
        {
            ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 2, duration: 30),
            ClosedCycle(taskId, "2026-11-18T08:01:00", cavities: 8, duration: 90, isSuccessful: false)
        };

        var result = ShiftClosureCycleMetrics.Calculate(cycles, taskId, ShiftStart, FactEnd);

        result.OutputQuantity.Should().Be(2);
        result.AverageFullCycleSeconds.Should().Be(30);
    }

    [Fact]
    public void Metrics_exclude_other_tasks()
    {
        var taskId = Guid.NewGuid();
        var cycles = new[]
        {
            ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 2, duration: 30),
            ClosedCycle(Guid.NewGuid(), "2026-11-18T08:01:00", cavities: 8, duration: 90),
            ClosedCycle(null, "2026-11-18T08:02:00", cavities: 16, duration: 120)
        };

        var result = ShiftClosureCycleMetrics.Calculate(cycles, taskId, ShiftStart, FactEnd);

        result.OutputQuantity.Should().Be(2);
        result.AverageFullCycleSeconds.Should().Be(30);
    }

    [Fact]
    public void Metrics_include_cycle_started_before_shift_and_closed_inside()
    {
        var taskId = Guid.NewGuid();
        var cycle = ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 3, duration: 30);
        cycle.StartTime = Utc("2026-11-18T07:59:30");

        var result = ShiftClosureCycleMetrics.Calculate(new[] { cycle }, taskId, ShiftStart, FactEnd);

        result.OutputQuantity.Should().Be(3);
        result.AverageFullCycleSeconds.Should().Be(30);
    }

    [Fact]
    public void Pause_duration_does_not_affect_average_full_cycle()
    {
        var taskId = Guid.NewGuid();
        var first = ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 1, duration: 30);
        first.PauseDurationMs = 100_000;
        var second = ClosedCycle(taskId, "2026-11-18T08:01:00", cavities: 1, duration: 40);
        second.PauseDurationMs = 200_000;

        var result = ShiftClosureCycleMetrics.Calculate(new[] { first, second }, taskId, ShiftStart, FactEnd);

        result.AverageFullCycleSeconds.Should().Be(35);
    }

    [Fact]
    public void Zero_cycle_cavities_fall_back_to_mold_cavities()
    {
        var taskId = Guid.NewGuid();
        var cycle = ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 0, duration: 30);
        cycle.Mold = new Mold { Cavities = 6 };

        var result = ShiftClosureCycleMetrics.Calculate(new[] { cycle }, taskId, ShiftStart, FactEnd);

        result.OutputQuantity.Should().Be(6);
    }

    [Fact]
    public void No_matching_cycles_return_empty_metrics()
    {
        var result = ShiftClosureCycleMetrics.Calculate(
            Array.Empty<ImmCycle>(), Guid.NewGuid(), ShiftStart, FactEnd);

        result.OutputQuantity.Should().Be(0);
        result.AverageFullCycleSeconds.Should().BeNull();
    }

    [Fact]
    public void Previous_output_uses_only_cycles_closed_strictly_before_shift_start()
    {
        var taskId = Guid.NewGuid();
        var before = ClosedCycle(taskId, "2026-11-18T07:59:59", cavities: 2, duration: 20);
        var atStart = ClosedCycle(taskId, "2026-11-18T08:00:00", cavities: 4, duration: 30);
        var unsuccessful = ClosedCycle(
            taskId, "2026-11-18T07:00:00", cavities: 8, duration: 40, isSuccessful: false);
        var otherTask = ClosedCycle(Guid.NewGuid(), "2026-11-18T07:00:00", cavities: 16, duration: 50);

        ShiftClosureCycleMetrics.PreviousOutput(
                new[] { before, atStart, unsuccessful, otherTask, OpenCycle(taskId, 32) }, taskId, ShiftStart)
            .Should().Be(2);
    }

    private static ImmCycle ClosedCycle(
        Guid? taskId, string endTime, int cavities, int duration, bool isSuccessful = true)
    {
        var end = Utc(endTime);
        return new ImmCycle
        {
            TaskId = taskId,
            StartTime = end.AddSeconds(-duration),
            EndTime = end,
            DurationSeconds = duration,
            Cavities = cavities,
            IsSuccessful = isSuccessful
        };
    }

    private static ImmCycle OpenCycle(Guid? taskId, int cavities) => new()
    {
        TaskId = taskId,
        StartTime = ShiftStart,
        EndTime = null,
        DurationSeconds = 60,
        Cavities = cavities,
        IsSuccessful = true
    };

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
