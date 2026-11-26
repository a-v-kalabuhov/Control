using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public enum ShiftLeadSource
{
    Explicit,
    Default,
    ManagerFallback
}

public sealed record EffectiveShiftLead(string? Id, ShiftLeadSource? Source);

public sealed record ShiftLeadFallbackSnapshot(
    IReadOnlyDictionary<int, string> DefaultLeadIdsByPosition,
    string? FirstActiveManagerId,
    IReadOnlyDictionary<string, User> Users);

public static class EffectiveShiftLeadResolver
{
    public static EffectiveShiftLead Resolve(ShiftPlanItem item, ShiftLeadFallbackSnapshot snapshot)
    {
        if (item.ShiftLeadId != null)
            return new EffectiveShiftLead(item.ShiftLeadId, ShiftLeadSource.Explicit);

        if (snapshot.DefaultLeadIdsByPosition.TryGetValue(item.Position, out var defaultLeadId))
            return new EffectiveShiftLead(defaultLeadId, ShiftLeadSource.Default);

        if (snapshot.FirstActiveManagerId != null)
            return new EffectiveShiftLead(snapshot.FirstActiveManagerId, ShiftLeadSource.ManagerFallback);

        return new EffectiveShiftLead(null, null);
    }
}
