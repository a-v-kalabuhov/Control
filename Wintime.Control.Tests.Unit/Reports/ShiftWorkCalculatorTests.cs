using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Tests.Unit.Reports;

public class ShiftWorkCalculatorTests
{
    // Дневная смена: 08:00–16:00 (480 мин), обед 30 мин
    private static Shift DayShift() => new()
    {
        Id = Guid.NewGuid(),
        StartMinutes = 480,
        DurationMinutes = 480,
        BreakStartMinutes = 720,
        BreakDurationMinutes = 30
    };

    // Ночная смена: 22:00–06:00 (+8 ч = 480 мин), обед 30 мин
    private static Shift NightShift() => new()
    {
        Id = Guid.NewGuid(),
        StartMinutes = 1320,
        DurationMinutes = 480,
        BreakStartMinutes = 120,
        BreakDurationMinutes = 30
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Utc);

    // ── GetWorkedShiftInstances ─────────────────────────────────────────

    [Fact]
    public void NoTasks_ReturnsEmptySet()
    {
        var shifts = new[] { DayShift() };
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([], shifts);
        Assert.Empty(result);
    }

    [Fact]
    public void NullStartedAt_IsIgnored()
    {
        var shifts = new[] { DayShift() };
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([null], shifts);
        Assert.Empty(result);
    }

    [Fact]
    public void TaskInDayShift_ReturnsOneInstance()
    {
        var shift = DayShift();
        var startedAt = Utc(2026, 6, 1, 10, 0); // 10:00 — внутри дневной смены
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Single(result);
        Assert.Equal(new ShiftInstance(shift.Id, new DateTime(2026, 6, 1)), result.First());
    }

    [Fact]
    public void TaskBeforeShift_IsNotMatched()
    {
        var shift = DayShift(); // 08:00–16:00
        var startedAt = Utc(2026, 6, 1, 7, 59);
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Empty(result);
    }

    [Fact]
    public void TaskAtShiftEnd_IsNotMatched()
    {
        var shift = DayShift(); // до 16:00 (не включительно)
        var startedAt = Utc(2026, 6, 1, 16, 0);
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Empty(result);
    }

    [Fact]
    public void TwoTasksInSameShiftSameDay_CountAsOneInstance()
    {
        var shift = DayShift();
        DateTime?[] tasks = [Utc(2026, 6, 1, 9, 0), Utc(2026, 6, 1, 14, 0)];
        var result = ShiftWorkCalculator.GetWorkedShiftInstances(tasks, [shift]);
        Assert.Single(result);
    }

    [Fact]
    public void TasksOnDifferentDays_CountAsTwoInstances()
    {
        var shift = DayShift();
        DateTime?[] tasks = [Utc(2026, 6, 1, 9, 0), Utc(2026, 6, 2, 9, 0)];
        var result = ShiftWorkCalculator.GetWorkedShiftInstances(tasks, [shift]);
        Assert.Equal(2, result.Count);
    }

    // ── Ночная смена (переход через полночь) ────────────────────────────

    [Fact]
    public void NightShift_TaskBeforeMidnight_ShiftDateEqualsTaskDate()
    {
        var shift = NightShift(); // 22:00–06:00
        var startedAt = Utc(2026, 6, 1, 23, 0); // до полуночи
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 6, 1), result.First().ShiftDate);
    }

    [Fact]
    public void NightShift_TaskAfterMidnight_ShiftDateIsPreviousDay()
    {
        var shift = NightShift(); // 22:00–06:00, после полуночи до 06:00
        var startedAt = Utc(2026, 6, 2, 2, 0); // после полуночи
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Single(result);
        Assert.Equal(new DateTime(2026, 6, 1), result.First().ShiftDate); // смена началась 1-го
    }

    [Fact]
    public void NightShift_TasksBeforeAndAfterMidnight_SameShiftInstance()
    {
        var shift = NightShift();
        DateTime?[] tasks = [Utc(2026, 6, 1, 23, 0), Utc(2026, 6, 2, 3, 0)];
        var result = ShiftWorkCalculator.GetWorkedShiftInstances(tasks, [shift]);
        Assert.Single(result);
    }

    [Fact]
    public void NightShift_TaskAtExactlyEndTime_IsNotMatched()
    {
        var shift = NightShift(); // заканчивается в 06:00 (1320+480-1440=360 мин)
        var startedAt = Utc(2026, 6, 2, 6, 0);
        var result = ShiftWorkCalculator.GetWorkedShiftInstances([startedAt], [shift]);
        Assert.Empty(result);
    }

    // ── CalculateWorkSeconds ─────────────────────────────────────────────

    [Fact]
    public void CalculateWorkSeconds_OneShift_ReturnsCorrectSeconds()
    {
        var shift = DayShift(); // 480 мин - 30 мин = 450 мин = 27000 сек
        var startedAt = Utc(2026, 6, 1, 10, 0);
        var result = ShiftWorkCalculator.CalculateWorkSeconds([startedAt], [shift]);
        Assert.Equal(450 * 60, result);
    }

    [Fact]
    public void CalculateWorkSeconds_TwoShiftInstances_ReturnsSumOfBoth()
    {
        var shift = DayShift(); // 450 мин каждая
        DateTime?[] tasks = [Utc(2026, 6, 1, 10, 0), Utc(2026, 6, 2, 10, 0)];
        var result = ShiftWorkCalculator.CalculateWorkSeconds(tasks, [shift]);
        Assert.Equal(2 * 450 * 60, result);
    }

    [Fact]
    public void CalculateWorkSeconds_NoMatchingShift_ReturnsZero()
    {
        var shift = DayShift();
        var startedAt = Utc(2026, 6, 1, 3, 0); // 03:00 — не попадает ни в одну смену
        var result = ShiftWorkCalculator.CalculateWorkSeconds([startedAt], [shift]);
        Assert.Equal(0, result);
    }

    // ── CountWorkedShifts ────────────────────────────────────────────────

    [Fact]
    public void CountWorkedShifts_ThreeTasksInTwoShifts_ReturnsTwo()
    {
        var shift = DayShift();
        DateTime?[] tasks =
        [
            Utc(2026, 6, 1, 9, 0),
            Utc(2026, 6, 1, 14, 0), // дубликат — та же смена
            Utc(2026, 6, 2, 9, 0)
        ];
        var result = ShiftWorkCalculator.CountWorkedShifts(tasks, [shift]);
        Assert.Equal(2, result);
    }
}
