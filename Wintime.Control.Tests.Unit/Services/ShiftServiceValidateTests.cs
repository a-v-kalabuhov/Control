using FluentAssertions;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Infrastructure.Services;

namespace Wintime.Control.Tests.Unit.Services;

public class ShiftServiceValidateTests
{
    private static ShiftService CreateSut() => new(null!);

    private static ShiftScheduleItemDto Shift(
        int startMinutes,
        int durationMinutes,
        int breakStartMinutes = 0,
        int breakDurationMinutes = 0) => new()
    {
        StartMinutes = startMinutes,
        DurationMinutes = durationMinutes,
        BreakStartMinutes = breakStartMinutes,
        BreakDurationMinutes = breakDurationMinutes
    };

    // =========================================================================
    // Валидные расписания — ошибок быть не должно
    // =========================================================================

    /// <summary>
    /// Одна дневная смена с перерывом — все параметры корректны, ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_SingleShiftWithBreak_ReturnsNoErrors()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 540, 720, 60) }; // 08:00–17:00, перерыв 12:00–13:00

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Одна смена без перерыва — ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_SingleShiftNoBreak_ReturnsNoErrors()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480) }; // 08:00–16:00

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Две смены, границы которых ровно совпадают (вторая начинается в момент окончания первой) —
    /// пересечение не фиксируется, ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_TwoShiftsTouchingBoundaries_ReturnsNoErrors()
    {
        var sut = CreateSut();
        var shifts = new[]
        {
            Shift(480, 480),  // 08:00–16:00
            Shift(960, 480)   // 16:00–00:00
        };

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Ночная смена с перерывом до полуночи — корректная конфигурация, ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_NightShiftWithBreakBeforeMidnight_ReturnsNoErrors()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(1320, 480, 1380, 30) }; // 22:00–06:00, перерыв 23:00–23:30

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Ночная смена с перерывом после полуночи — время начала перерыва меньше времени начала смены,
    /// но нормализация (+1440) корректно определяет положение внутри смены. Ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_NightShiftWithBreakAfterMidnight_ReturnsNoErrors()
    {
        var sut = CreateSut();
        // Смена 22:00–06:00 (480 мин), перерыв 01:00–01:30 (breakStart=60 < shiftStart=1320 → нормализуется)
        var shifts = new[] { Shift(1320, 480, 60, 30) };

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    // =========================================================================
    // Валидация полей — одиночные нарушения
    // =========================================================================

    /// <summary>
    /// Время начала смены меньше нуля — ошибка валидации поля.
    /// </summary>
    [Fact]
    public void Validate_StartMinutesNegative_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(-1, 480) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("время начала"));
    }

    /// <summary>
    /// Время начала смены больше 1439 — ошибка валидации поля.
    /// </summary>
    [Fact]
    public void Validate_StartMinutesAboveMax_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(1440, 480) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("время начала"));
    }

    /// <summary>
    /// Продолжительность смены меньше минимума (30 мин) — ошибка.
    /// </summary>
    [Fact]
    public void Validate_DurationTooShort_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 29) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("продолжительность"));
    }

    /// <summary>
    /// Продолжительность смены больше максимума (1380 мин = 23 ч) — ошибка.
    /// </summary>
    [Fact]
    public void Validate_DurationTooLong_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 1381) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("продолжительность"));
    }

    /// <summary>
    /// Продолжительность перерыва отрицательная — ошибка.
    /// </summary>
    [Fact]
    public void Validate_BreakDurationNegative_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480, 720, -1) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("перерыва не может быть отрицательной"));
    }

    // =========================================================================
    // Валидация перерыва
    // =========================================================================

    /// <summary>
    /// Перерыв начинается раньше начала смены. Нормализация трактует breakStart &lt; shiftStart
    /// как «после полуночи» и добавляет 1440, из-за чего эффективный конец перерыва
    /// выходит далеко за пределы смены. Ошибка всё равно должна быть возвращена.
    /// </summary>
    [Fact]
    public void Validate_BreakStartsBeforeShift_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480, 420, 30) }; // смена 08:00, перерыв в 07:00

        var errors = sut.Validate(shifts);

        errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Перерыв выходит за пределы окончания смены на 1 минуту — ошибка.
    /// Смена 08:00–16:00 (960 мин), перерыв 15:31–16:01 (931+30=961 > 960).
    /// </summary>
    [Fact]
    public void Validate_BreakExceedsShiftEnd_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480, 931, 30) }; // перерыв 15:31–16:01, смена заканчивается в 16:00

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("перерыв выходит за пределы"));
    }

    /// <summary>
    /// Длительность перерыва равна длительности смены — ошибка.
    /// </summary>
    [Fact]
    public void Validate_BreakDurationEqualsShiftDuration_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480, 480, 480) };

        var errors = sut.Validate(shifts);

        errors.Should().Contain(e => e.Contains("длительность перерыва не может превышать"));
    }

    /// <summary>
    /// Время начала перерыва вне диапазона 0–1439 при ненулевой длительности — ошибка.
    /// </summary>
    [Fact]
    public void Validate_BreakStartOutOfRange_ReturnsError()
    {
        var sut = CreateSut();
        var shifts = new[] { Shift(480, 480, 1440, 30) };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("время начала перерыва"));
    }

    // =========================================================================
    // Пересечения смен
    // =========================================================================

    /// <summary>
    /// Два соседних интервала пересекаются — ошибка пересечения.
    /// </summary>
    [Fact]
    public void Validate_TwoShiftsOverlap_ReturnsOverlapError()
    {
        var sut = CreateSut();
        var shifts = new[]
        {
            Shift(480, 480),  // 08:00–16:00
            Shift(900, 480)   // 15:00–23:00 — пересечение с первой
        };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("пересекаются"));
    }

    /// <summary>
    /// Ночная смена переходит через полночь и перекрывает начало первой смены — ошибка.
    /// 22:00 + 12ч = 10:00 следующего дня; первая смена начинается в 08:00.
    /// 1320 + 720 = 2040 > 480 + 1440 = 1920 → пересечение.
    /// </summary>
    [Fact]
    public void Validate_NightShiftWrapsAndOverlapsFirstShift_ReturnsOverlapError()
    {
        var sut = CreateSut();
        var shifts = new[]
        {
            Shift(480, 120),   // 08:00–10:00
            Shift(1320, 720)   // 22:00–10:00 (12 ч через полночь)
        };

        var errors = sut.Validate(shifts);

        errors.Should().ContainSingle(e => e.Contains("пересекается"));
    }

    /// <summary>
    /// Ночная смена заканчивается ровно в момент начала первой смены — пересечения нет, ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_NightShiftEndsTouchingFirstShiftStart_ReturnsNoErrors()
    {
        var sut = CreateSut();
        // Ночная 22:00 + 600мин = 10ч → 08:00; first shift = 08:00
        // last.StartMinutes + last.DurationMinutes = 1320 + 600 = 1920
        // first.StartMinutes + 1440 = 480 + 1440 = 1920 → равно, не больше → нет пересечения
        var shifts = new[]
        {
            Shift(480, 120),   // 08:00–10:00
            Shift(1320, 600)   // 22:00–08:00 (ровно касается)
        };

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Несколько смен, все корректны и упорядочены — ошибок нет.
    /// </summary>
    [Fact]
    public void Validate_ThreeValidShifts_ReturnsNoErrors()
    {
        var sut = CreateSut();
        var shifts = new[]
        {
            Shift(0,   480),  // 00:00–08:00
            Shift(480, 480),  // 08:00–16:00
            Shift(960, 480)   // 16:00–00:00
        };

        var errors = sut.Validate(shifts);

        errors.Should().BeEmpty();
    }
}
