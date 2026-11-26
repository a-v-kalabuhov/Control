using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class EffectiveShiftLeadResolverTests
{
    [Fact]
    public void Resolve_ExplicitLead_WinsOverDefaultAndManagerFallback()
    {
        var item = Item("explicit-lead");
        var snapshot = Snapshot(defaultLeadId: "default-lead", managerId: "manager");

        var result = EffectiveShiftLeadResolver.Resolve(item, snapshot);

        result.Should().Be(new EffectiveShiftLead("explicit-lead", ShiftLeadSource.Explicit));
    }

    [Fact]
    public void Resolve_DefaultLead_WinsOverManagerFallbackWhenExplicitIsMissing()
    {
        var item = Item(explicitLeadId: null);
        var snapshot = Snapshot(defaultLeadId: "default-lead", managerId: "manager");

        var result = EffectiveShiftLeadResolver.Resolve(item, snapshot);

        result.Should().Be(new EffectiveShiftLead("default-lead", ShiftLeadSource.Default));
    }

    [Fact]
    public void Resolve_ManagerFallback_IsUsedWhenExplicitAndDefaultAreMissing()
    {
        var item = Item(explicitLeadId: null);
        var snapshot = Snapshot(defaultLeadId: null, managerId: "manager");

        var result = EffectiveShiftLeadResolver.Resolve(item, snapshot);

        result.Should().Be(new EffectiveShiftLead("manager", ShiftLeadSource.ManagerFallback));
    }

    [Fact]
    public void Resolve_NoAvailableLead_ReturnsEmptyResultWithoutSource()
    {
        var item = Item(explicitLeadId: null);
        var snapshot = Snapshot(defaultLeadId: null, managerId: null);

        var result = EffectiveShiftLeadResolver.Resolve(item, snapshot);

        result.Should().Be(new EffectiveShiftLead(null, null));
    }

    private static ShiftPlanItem Item(string? explicitLeadId) => new()
    {
        Position = 2,
        ShiftLeadId = explicitLeadId
    };

    private static ShiftLeadFallbackSnapshot Snapshot(string? defaultLeadId, string? managerId) => new(
        defaultLeadId == null
            ? new Dictionary<int, string>()
            : new Dictionary<int, string> { [2] = defaultLeadId },
        managerId,
        new Dictionary<string, User>());
}
