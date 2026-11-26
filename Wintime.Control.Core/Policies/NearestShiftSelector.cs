using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public static class NearestShiftSelector
{
    public static ShiftPlanItem? ForLead(
        IEnumerable<ShiftPlanItem> items,
        string userId,
        DateTime nowUtc,
        Func<ShiftPlanItem, string?>? effectiveLeadId = null)
    {
        effectiveLeadId ??= static item => item.ShiftLeadId;
        var ownItems = items.Where(item => effectiveLeadId(item) == userId).ToList();
        return ownItems
            .Where(item => item.StartAtUtc <= nowUtc && nowUtc < item.EndAtUtc)
            .OrderBy(item => item.StartAtUtc)
            .ThenBy(item => item.Position)
            .FirstOrDefault()
            ?? ownItems
                .Where(item => nowUtc < item.StartAtUtc)
                .OrderBy(item => item.StartAtUtc)
                .ThenBy(item => item.Position)
                .FirstOrDefault();
    }

    public static bool IsNearestForManager(
        IEnumerable<ShiftPlanItem> savedItems,
        ShiftPlanItem candidate,
        DateTime nowUtc,
        Func<ShiftPlanItem, string?>? effectiveLeadId = null)
    {
        effectiveLeadId ??= static item => item.ShiftLeadId;
        if (effectiveLeadId(candidate) == null || nowUtc >= candidate.EndAtUtc) return false;

        var positionItems = savedItems
            .Where(item => item.Position == candidate.Position && effectiveLeadId(item) != null)
            .ToList();
        var nearest = positionItems
            .Where(item => item.StartAtUtc <= nowUtc && nowUtc < item.EndAtUtc)
            .OrderBy(item => item.StartAtUtc)
            .ThenBy(item => item.Position)
            .FirstOrDefault()
            ?? positionItems
                .Where(item => nowUtc < item.StartAtUtc)
                .OrderBy(item => item.StartAtUtc)
                .ThenBy(item => item.Position)
                .FirstOrDefault();

        return nearest != null && RepresentsSameShift(nearest, candidate);
    }

    public static bool RepresentsSameShift(ShiftPlanItem left, ShiftPlanItem right)
    {
        if (left.Id != Guid.Empty || right.Id != Guid.Empty)
            return left.Id != Guid.Empty && left.Id == right.Id;

        return ReferenceEquals(left, right) ||
            (left.Day?.ShiftDate == right.Day?.ShiftDate && left.Position == right.Position);
    }
}
