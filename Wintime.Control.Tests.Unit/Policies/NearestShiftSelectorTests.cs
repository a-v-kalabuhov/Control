using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class NearestShiftSelectorTests
{
    [Fact]
    public void ForLead_prefers_current_then_earliest_future_then_null()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var current = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var future = Item("lead", "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
        var later = Item("lead", "2026-11-10T08:00:00Z", "2026-11-10T16:00:00Z", 1);
        var foreign = Item("other", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 2);

        NearestShiftSelector.ForLead(new[] { later, foreign, future, current }, "lead", now).Should().BeSameAs(current);
        NearestShiftSelector.ForLead(new[] { later, future }, "lead", now).Should().BeSameAs(future);
        NearestShiftSelector.ForLead(new[] { foreign }, "lead", now).Should().BeNull();
    }

    [Fact]
    public void ForLead_moves_to_next_column_of_same_person_after_end()
    {
        var now = Utc("2026-11-08T16:00:00Z");
        var ended = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var next = Item("lead", "2026-11-08T16:00:00Z", "2026-11-09T00:00:00Z", 2);
        NearestShiftSelector.ForLead(new[] { ended, next }, "lead", now).Should().BeSameAs(next);
    }

    [Fact]
    public void IsNearestForManager_allows_only_current_item_with_lead()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var current = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var future = Item("lead", "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
        var savedItems = new[] { future, current };

        NearestShiftSelector.IsNearestForManager(savedItems, current, now).Should().BeTrue();
        NearestShiftSelector.IsNearestForManager(savedItems, future, now).Should().BeFalse();
    }

    [Fact]
    public void IsNearestForManager_without_current_allows_only_earliest_future_of_same_position()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var historical = Item("lead", "2026-11-07T08:00:00Z", "2026-11-07T16:00:00Z", 1);
        var earliest = Item("lead", "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
        var later = Item("lead", "2026-11-10T08:00:00Z", "2026-11-10T16:00:00Z", 1);
        var otherPosition = Item("lead", "2026-11-08T16:00:00Z", "2026-11-09T00:00:00Z", 2);
        var savedItems = new[] { later, otherPosition, historical, earliest };

        NearestShiftSelector.IsNearestForManager(savedItems, earliest, now).Should().BeTrue();
        NearestShiftSelector.IsNearestForManager(savedItems, later, now).Should().BeFalse();
        NearestShiftSelector.IsNearestForManager(savedItems, otherPosition, now).Should().BeTrue();
    }

    [Fact]
    public void IsNearestForManager_rejects_historical_item()
    {
        var now = Utc("2026-11-08T16:00:00Z");
        var historical = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);

        NearestShiftSelector.IsNearestForManager(new[] { historical }, historical, now).Should().BeFalse();
    }

    [Fact]
    public void IsNearestForManager_rejects_column_without_lead()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var current = Item(null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);

        NearestShiftSelector.IsNearestForManager(new[] { current }, current, now).Should().BeFalse();
    }

    [Fact]
    public void IsNearestForManager_allows_detached_candidate_with_same_persistent_id()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var saved = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var candidate = Item("lead", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        candidate.Id = saved.Id;

        NearestShiftSelector.IsNearestForManager(new[] { saved }, candidate, now).Should().BeTrue();
    }

    [Fact]
    public void ForLead_uses_effective_lead_id_instead_of_explicit_value()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var explicitOther = Item("other", "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var defaultOwn = Item(null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 2);
        var managerFallback = Item(null, "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
        var effectiveIds = new Dictionary<ShiftPlanItem, string?>
        {
            [explicitOther] = "other",
            [defaultOwn] = "lead",
            [managerFallback] = "manager"
        };
        string? EffectiveId(ShiftPlanItem item) => effectiveIds[item];

        NearestShiftSelector.ForLead(new[] { managerFallback, explicitOther, defaultOwn }, "lead", now, EffectiveId)
            .Should().BeSameAs(defaultOwn);
        NearestShiftSelector.ForLead(new[] { managerFallback }, "lead", now, EffectiveId)
            .Should().BeNull("ManagerFallback принадлежит менеджеру, а не текущему ShiftLead");
    }

    [Fact]
    public void IsNearestForManager_uses_effective_ids_and_rejects_missing_lead()
    {
        var now = Utc("2026-11-08T12:00:00Z");
        var current = Item(null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 1);
        var future = Item(null, "2026-11-09T08:00:00Z", "2026-11-09T16:00:00Z", 1);
        var withoutLead = Item(null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", 2);
        var savedItems = new[] { future, current, withoutLead };
        string? EffectiveId(ShiftPlanItem item) => ReferenceEquals(item, withoutLead) ? null : "manager";

        NearestShiftSelector.IsNearestForManager(savedItems, current, now, EffectiveId).Should().BeTrue();
        NearestShiftSelector.IsNearestForManager(savedItems, future, now, EffectiveId).Should().BeFalse();
        NearestShiftSelector.IsNearestForManager(savedItems, withoutLead, now, EffectiveId).Should().BeFalse();
    }

    private static ShiftPlanItem Item(string? shiftLeadId, string start, string end, int position) => new()
    {
        ShiftLeadId = shiftLeadId,
        StartAtUtc = Utc(start),
        EndAtUtc = Utc(end),
        Position = position
    };

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
