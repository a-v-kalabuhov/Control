using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftPlanSynchronizerTests
{
    [Fact]
    public void Apply_preserves_ids_leads_and_assignments_on_remaining_positions()
    {
        var day = DayWithTwoItems();
        var firstId = day.Items[0].Id;
        var firstLead = day.Items[0].ShiftLeadId;
        var firstAssignment = day.Items[0].OperatorAssignments.Single();
        var windows = new[]
        {
            Window(1, "2026-11-10T06:00:00Z", "2026-11-10T14:00:00Z"),
            Window(2, "2026-11-10T14:00:00Z", "2026-11-10T22:00:00Z"),
            Window(3, "2026-11-10T22:00:00Z", "2026-11-11T06:00:00Z")
        };

        var removed = ShiftPlanSynchronizer.Apply(day, windows);

        day.Items.Should().HaveCount(3);
        day.Items[0].Id.Should().Be(firstId);
        day.Items[0].ShiftLeadId.Should().Be(firstLead);
        day.Items[0].OperatorAssignments.Should().ContainSingle().Which.Should().BeSameAs(firstAssignment);
        day.Items[0].StartAtUtc.Should().Be(windows[0].StartAtUtc);
        day.Items[2].ShiftLeadId.Should().BeNull();
        day.Items[2].OperatorAssignments.Should().BeEmpty();
        removed.Should().BeEmpty();
    }

    [Fact]
    public void Apply_returns_removed_positions_with_their_assignments()
    {
        var day = DayWithThreeItems();
        var second = day.Items.Single(i => i.Position == 2);
        var third = day.Items.Single(i => i.Position == 3);
        var secondAssignment = second.OperatorAssignments.Single();
        var thirdAssignment = third.OperatorAssignments.Single();
        var firstId = day.Items.Single(i => i.Position == 1).Id;

        var removed = ShiftPlanSynchronizer.Apply(day,
        [
            Window(1, "2026-11-10T06:00:00Z", "2026-11-10T14:00:00Z")
        ]);

        day.Items.Should().ContainSingle();
        day.Items[0].Id.Should().Be(firstId);
        removed.Should().BeEquivalentTo(new[] { second, third });
        removed.SelectMany(i => i.OperatorAssignments).Should()
            .BeEquivalentTo(new[] { secondAssignment, thirdAssignment });
    }

    [Fact]
    public void Apply_with_same_position_count_updates_only_windows()
    {
        var day = DayWithTwoItems();
        var before = day.Items.Select(i => new
        {
            Item = i,
            i.Id,
            i.ShiftPlanDayId,
            i.Position,
            i.ShiftLeadId,
            Assignments = i.OperatorAssignments.ToArray()
        }).ToArray();
        var windows = new[]
        {
            Window(1, "2026-12-01T03:00:00Z", "2026-12-01T11:00:00Z", "Europe/Moscow"),
            Window(2, "2026-12-01T11:00:00Z", "2026-12-01T19:00:00Z", "Europe/Moscow")
        };

        var removed = ShiftPlanSynchronizer.Apply(day, windows);

        removed.Should().BeEmpty();
        day.Items.Select(i => i.Position).Should().Equal(1, 2);
        for (var index = 0; index < before.Length; index++)
        {
            var item = day.Items[index];
            item.Should().BeSameAs(before[index].Item);
            item.Id.Should().Be(before[index].Id);
            item.ShiftPlanDayId.Should().Be(before[index].ShiftPlanDayId);
            item.Position.Should().Be(before[index].Position);
            item.ShiftLeadId.Should().Be(before[index].ShiftLeadId);
            item.OperatorAssignments.Should().Equal(before[index].Assignments);
            item.StartAtUtc.Should().Be(windows[index].StartAtUtc);
            item.EndAtUtc.Should().Be(windows[index].EndAtUtc);
            item.TimeZoneId.Should().Be(windows[index].TimeZoneId);
        }
    }

    [Fact]
    public void Apply_with_empty_schedule_removes_every_position_including_leads()
    {
        var day = DayWithThreeItems();
        var originalItems = day.Items.ToArray();

        var removed = ShiftPlanSynchronizer.Apply(day, []);

        day.Items.Should().BeEmpty();
        removed.Should().BeEquivalentTo(originalItems);
        day.Items.Any(i => i.ShiftLeadId != null).Should().BeFalse();
    }

    private static ShiftPlanDay DayWithTwoItems()
    {
        var day = new ShiftPlanDay { ShiftDate = new DateOnly(2026, 11, 10) };
        day.Items.Add(Item(day, 1, "lead-1", withAssignment: true));
        day.Items.Add(Item(day, 2, "lead-2"));
        return day;
    }

    private static ShiftPlanDay DayWithThreeItems()
    {
        var day = DayWithTwoItems();
        day.Items.Add(Item(day, 3, "lead-3", withAssignment: true));
        day.Items.Single(i => i.Position == 2).OperatorAssignments.Add(Assignment(day.Items[1]));
        return day;
    }

    private static ShiftPlanItem Item(
        ShiftPlanDay day, int position, string? leadId, bool withAssignment = false)
    {
        var item = new ShiftPlanItem
        {
            ShiftPlanDayId = day.Id,
            Day = day,
            Position = position,
            StartAtUtc = Utc("2026-11-10T00:00:00Z").AddHours(position),
            EndAtUtc = Utc("2026-11-10T00:00:00Z").AddHours(position + 8),
            TimeZoneId = "Europe/Moscow",
            ShiftLeadId = leadId
        };
        if (withAssignment) item.OperatorAssignments.Add(Assignment(item));
        return item;
    }

    private static ShiftImmOperatorAssignment Assignment(ShiftPlanItem item) => new()
    {
        ShiftPlanItemId = item.Id,
        ShiftPlanItem = item,
        ImmId = Guid.NewGuid(),
        OperatorId = $"operator-{item.Position}",
        Slot = 1,
        AssignedAt = Utc("2026-11-09T12:00:00Z")
    };

    private static ShiftWindow Window(
        int position, string start, string end, string timeZoneId = "Europe/Moscow") =>
        new(position, Utc(start), Utc(end), timeZoneId);

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
