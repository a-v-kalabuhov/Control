using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

public static class ShiftPlanSynchronizer
{
    public static List<ShiftPlanItem> Apply(ShiftPlanDay day, IReadOnlyList<ShiftWindow> windows)
    {
        var existingByPosition = day.Items.ToDictionary(i => i.Position);
        var remainingPositions = windows.Select(w => w.Position).ToHashSet();
        var removed = day.Items.Where(i => !remainingPositions.Contains(i.Position)).ToList();
        var synchronized = new List<ShiftPlanItem>(windows.Count);

        foreach (var window in windows)
        {
            if (!existingByPosition.TryGetValue(window.Position, out var item)) continue;

            var startAtUtc = DateTime.SpecifyKind(window.StartAtUtc, DateTimeKind.Utc);
            var endAtUtc = DateTime.SpecifyKind(window.EndAtUtc, DateTimeKind.Utc);
            foreach (var assignment in item.OperatorAssignments)
            {
                var assignedAtUtc = DateTime.SpecifyKind(assignment.AssignedAt, DateTimeKind.Utc);
                var unassignedAtUtc = DateTime.SpecifyKind(
                    assignment.UnassignedAt ?? item.EndAtUtc, DateTimeKind.Utc);
                if (assignedAtUtc < startAtUtc || unassignedAtUtc > endAtUtc)
                    throw new DomainException(
                        "Интервал литейщика выходит за новые границы смены; измените график или интервал");
            }
        }

        foreach (var window in windows.OrderBy(w => w.Position))
        {
            if (!existingByPosition.TryGetValue(window.Position, out var item))
            {
                item = new ShiftPlanItem
                {
                    ShiftPlanDayId = day.Id,
                    Day = day,
                    Position = window.Position
                };
            }

            item.StartAtUtc = window.StartAtUtc;
            item.EndAtUtc = window.EndAtUtc;
            item.TimeZoneId = window.TimeZoneId;
            synchronized.Add(item);
        }

        day.Items.Clear();
        day.Items.AddRange(synchronized);
        return removed;
    }
}
