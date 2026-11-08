using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public static class NearestShiftSelector
{
    public static ShiftPlanItem? ForLead(IEnumerable<ShiftPlanItem> items, string userId, DateTime nowUtc)
    {
        var ownItems = items.Where(item => item.ShiftLeadId == userId).ToList();
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
        DateTime nowUtc)
    {
        if (candidate.ShiftLeadId == null || nowUtc >= candidate.EndAtUtc) return false;

        var positionItems = savedItems
            .Where(item => item.Position == candidate.Position && item.ShiftLeadId != null)
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

        return nearest?.Id == candidate.Id;
    }
}
