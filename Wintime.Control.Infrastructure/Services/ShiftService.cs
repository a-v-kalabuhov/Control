using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class ShiftService : IShiftService
{
    private readonly ControlDbContext _context;

    public ShiftService(ControlDbContext context) => _context = context;

    public async Task<IReadOnlyList<ShiftDto>> GetShiftsAsync()
    {
        var shifts = await _context.Shifts
            .OrderBy(s => s.StartMinutes)
            .ToListAsync();

        return shifts.Select((s, i) => ToDto(s, i + 1)).ToList();
    }

    public IReadOnlyList<string> Validate(IReadOnlyList<ShiftScheduleItemDto> shifts)
    {
        var errors = new List<string>();

        for (int i = 0; i < shifts.Count; i++)
        {
            var s = shifts[i];
            var num = i + 1;

            if (s.StartMinutes < 0 || s.StartMinutes > 1439)
                errors.Add($"Смена {num}: время начала должно быть от 00:00 до 23:59");

            if (s.DurationMinutes < 30 || s.DurationMinutes > 1380)
                errors.Add($"Смена {num}: продолжительность должна быть от 30 минут до 23 часов");

            if (s.BreakDurationMinutes < 0)
                errors.Add($"Смена {num}: продолжительность перерыва не может быть отрицательной");

            if (s.BreakDurationMinutes > 0)
            {
                if (s.BreakStartMinutes < 0 || s.BreakStartMinutes > 1439)
                {
                    errors.Add($"Смена {num}: время начала перерыва должно быть от 00:00 до 23:59");
                }
                else
                {
                    // Нормализуем начало перерыва относительно начала смены
                    // (в ночной смене перерыв может быть после полуночи, т.е. breakStart < shiftStart)
                    var effectiveBreakStart = s.BreakStartMinutes >= s.StartMinutes
                        ? s.BreakStartMinutes
                        : s.BreakStartMinutes + 1440;

                    if (effectiveBreakStart < s.StartMinutes)
                        errors.Add($"Смена {num}: перерыв начинается раньше начала смены");
                    else if (effectiveBreakStart + s.BreakDurationMinutes > s.StartMinutes + s.DurationMinutes)
                        errors.Add($"Смена {num}: перерыв выходит за пределы смены");
                    else if (s.BreakDurationMinutes >= s.DurationMinutes)
                        errors.Add($"Смена {num}: длительность перерыва не может превышать длительность смены");
                }
            }
        }

        if (errors.Count > 0)
            return errors;

        // Проверка пересечений (с учётом ночных смен, переходящих через полночь)
        var sorted = shifts.OrderBy(s => s.StartMinutes).ToList();
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var curr = sorted[i];
            var next = sorted[i + 1];
            if (curr.StartMinutes + curr.DurationMinutes > next.StartMinutes)
                errors.Add(
                    $"Смены пересекаются: {FormatTime(curr.StartMinutes)}–{FormatTime(curr.StartMinutes + curr.DurationMinutes)} " +
                    $"и {FormatTime(next.StartMinutes)}–{FormatTime(next.StartMinutes + next.DurationMinutes)}");
        }

        if (sorted.Count > 1)
        {
            var last = sorted[^1];
            var first = sorted[0];
            if (last.StartMinutes + last.DurationMinutes > first.StartMinutes + 1440)
                errors.Add(
                    $"Ночная смена {FormatTime(last.StartMinutes)}–{FormatTime(last.StartMinutes + last.DurationMinutes)} " +
                    $"пересекается с первой сменой {FormatTime(first.StartMinutes)}");
        }

        return errors;
    }

    public async Task<IReadOnlyList<ShiftDto>> SaveShiftsAsync(IReadOnlyList<ShiftScheduleItemDto> shifts)
    {
        var existing = await _context.Shifts.ToListAsync();
        _context.Shifts.RemoveRange(existing);

        var newShifts = shifts
            .OrderBy(s => s.StartMinutes)
            .Select(s => new Shift
            {
                StartMinutes = s.StartMinutes,
                DurationMinutes = s.DurationMinutes,
                BreakStartMinutes = s.BreakStartMinutes,
                BreakDurationMinutes = s.BreakDurationMinutes
            })
            .ToList();

        _context.Shifts.AddRange(newShifts);
        await _context.SaveChangesAsync();

        return newShifts.Select((s, i) => ToDto(s, i + 1)).ToList();
    }

    private static ShiftDto ToDto(Shift s, int number) => new()
    {
        Id = s.Id,
        Number = number,
        StartMinutes = s.StartMinutes,
        DurationMinutes = s.DurationMinutes,
        BreakStartMinutes = s.BreakStartMinutes,
        BreakDurationMinutes = s.BreakDurationMinutes,
        StartTime = FormatTime(s.StartMinutes),
        EndTime = FormatTime(s.StartMinutes + s.DurationMinutes),
        BreakStartTime = s.BreakDurationMinutes > 0 ? FormatTime(s.BreakStartMinutes) : null,
        BreakEndTime = s.BreakDurationMinutes > 0 ? FormatTime(s.BreakStartMinutes + s.BreakDurationMinutes) : null
    };

    private static string FormatTime(int totalMinutes) =>
        $"{totalMinutes / 60 % 24:D2}:{totalMinutes % 60:D2}";
}
