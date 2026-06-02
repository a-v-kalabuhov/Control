using Wintime.Control.Core.Entities;

namespace Wintime.Control.Infrastructure.Reports;

internal static class ShiftWorkCalculator
{
    internal static int CalculateWorkSeconds(IEnumerable<DateTime?> taskStartTimes, IReadOnlyList<Shift> shifts) =>
        GetWorkedShiftInstances(taskStartTimes, shifts)
            .Sum(inst =>
            {
                var shift = shifts.First(s => s.Id == inst.ShiftId);
                return (shift.DurationMinutes - shift.BreakDurationMinutes) * 60;
            });

    internal static int CountWorkedShifts(IEnumerable<DateTime?> taskStartTimes, IReadOnlyList<Shift> shifts) =>
        GetWorkedShiftInstances(taskStartTimes, shifts).Count;

    internal static HashSet<ShiftInstance> GetWorkedShiftInstances(
        IEnumerable<DateTime?> taskStartTimes, IReadOnlyList<Shift> shifts)
    {
        var instances = new HashSet<ShiftInstance>();

        foreach (var startedAt in taskStartTimes.Where(t => t.HasValue))
        {
            var dt = startedAt!.Value;
            var minutes = dt.Hour * 60 + dt.Minute;

            foreach (var shift in shifts)
            {
                var shiftEnd = shift.StartMinutes + shift.DurationMinutes;

                if (shiftEnd <= 1440)
                {
                    if (minutes >= shift.StartMinutes && minutes < shiftEnd)
                    {
                        instances.Add(new ShiftInstance(shift.Id, dt.Date));
                        break;
                    }
                }
                else
                {
                    // Смена переходит через полночь
                    if (minutes >= shift.StartMinutes)
                    {
                        instances.Add(new ShiftInstance(shift.Id, dt.Date));
                        break;
                    }
                    if (minutes < shiftEnd - 1440)
                    {
                        instances.Add(new ShiftInstance(shift.Id, dt.Date.AddDays(-1)));
                        break;
                    }
                }
            }
        }

        return instances;
    }
}

internal readonly record struct ShiftInstance(Guid ShiftId, DateTime ShiftDate);
