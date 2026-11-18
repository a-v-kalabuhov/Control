using FluentAssertions;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftOutputDistributorTests
{
    private static readonly Guid TaskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTaskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime ShiftStart = Utc("2026-11-18T08:00:00");
    private static readonly DateTime FactEnd = Utc("2026-11-18T10:00:00");

    [Fact]
    public void Adjacent_same_pair_is_merged_before_odd_split_and_slot_swap_does_not_split()
    {
        var a = Operator("A", slot: 1, "08:00", "09:00");
        var b = Operator("B", slot: 2, "08:00", "09:00");
        var aLater = Operator("A", slot: 2, "09:00", "10:00");
        var bLater = Operator("B", slot: 1, "09:00", "10:00");
        var cycles = new[] { Cycle("09:00", 1), Cycle("09:30", 1) };

        var result = ShiftOutputDistributor.Distribute(
            new[] { a, b, aLater, bLater }, cycles, OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().ContainSingle();
        result.MergedIntervals[0].StartUtc.Should().Be(ShiftStart);
        result.MergedIntervals[0].EndUtc.Should().Be(FactEnd);
        result.MergedIntervals[0].Operators.Single(o => o.OperatorId == "A").Slot.Should().Be(1);
        result.MergedIntervals[0].Operators.Single(o => o.OperatorId == "B").Slot.Should().Be(2);
        result.Shares.Where(s => s.OperatorId == "A").Sum(s => s.Quantity).Should().Be(1);
        result.Shares.Where(s => s.OperatorId == "B").Sum(s => s.Quantity).Should().Be(1);
        result.Shares.Should().OnlyContain(s => s.Quantity == 1);
        result.Shares.Should().OnlyContain(s => s.SlotAtIntervalStart ==
            (s.OperatorId == "A" ? 1 : 2));
    }

    [Fact]
    public void Odd_output_is_split_after_merging_not_per_elementary_interval()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "09:00"), Operator("B", 2, "08:00", "09:00"),
            Operator("A", 2, "09:00", "10:00"), Operator("B", 1, "09:00", "10:00")
        };
        var cycles = new[] { Cycle("08:00", 1), Cycle("08:30", 1), Cycle("09:30", 1) };

        var result = ShiftOutputDistributor.Distribute(
            assignments, cycles, OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().ContainSingle();
        result.Shares.Single(s => s.OperatorId == "A").Quantity.Should().Be(2);
        result.Shares.Single(s => s.OperatorId == "B").Quantity.Should().Be(1);
    }

    [Fact]
    public void One_operator_receives_all_output_and_preserves_identity()
    {
        var assignment = Operator("A", 1, "08:00", "10:00");

        var result = ShiftOutputDistributor.Distribute(
            new[] { assignment }, new[] { Cycle("08:30", 2), Cycle("09:30", 3) },
            OneTaskWindow(), ShiftStart, FactEnd);

        var share = result.Shares.Should().ContainSingle().Subject;
        share.OperatorId.Should().Be("A");
        share.FullName.Should().Be("Operator A");
        share.EmployeeId.Should().Be("EMP-A");
        share.SlotAtIntervalStart.Should().Be(1);
        share.Quantity.Should().Be(5);
        result.Orphans.Should().BeEmpty();
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 1)]
    public void Two_operators_split_even_and_odd_output_by_slot_at_interval_start(
        int quantity, int expectedFirst, int expectedSecond)
    {
        var assignments = new[]
        {
            Operator("B", 2, "08:00", "10:00"), Operator("A", 1, "08:00", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, new[] { Cycle("09:00", quantity) }, OneTaskWindow(), ShiftStart, FactEnd);

        result.Shares.Single(s => s.OperatorId == "A").Quantity.Should().Be(expectedFirst);
        result.Shares.Single(s => s.OperatorId == "B").Quantity.Should().Be(expectedSecond);
    }

    [Fact]
    public void Intersecting_task_window_creates_zero_shares_for_every_operator()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "10:00"), Operator("B", 2, "08:00", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, Array.Empty<ShiftOutputCycle>(), OneTaskWindow(start: "08:30", end: "09:30"),
            ShiftStart, FactEnd);

        result.Shares.Should().HaveCount(2);
        result.Shares.Should().OnlyContain(s => s.Quantity == 0);
        result.Shares.Should().OnlyContain(s => s.StartUtc == T("08:30") && s.EndUtc == T("09:30"));
        result.MergedIntervals.Should().ContainSingle();
        result.Orphans.Should().BeEmpty();
    }

    [Fact]
    public void Shares_of_same_operator_from_different_intervals_are_summed_without_loss()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "09:00"), Operator("B", 2, "08:00", "09:00"),
            Operator("C", 1, "09:00", "10:00"), Operator("A", 2, "09:00", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, new[] { Cycle("08:30", 3), Cycle("09:30", 3) },
            OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().HaveCount(2);
        result.Shares.Where(s => s.OperatorId == "A").Should().HaveCount(2);
        result.Shares.Where(s => s.OperatorId == "A").Sum(s => s.Quantity).Should().Be(3);
        result.Shares.Sum(s => s.Quantity).Should().Be(6);
    }

    [Fact]
    public void Gap_between_equal_operator_sets_is_not_merged()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "09:00"), Operator("A", 1, "09:30", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, Array.Empty<ShiftOutputCycle>(), OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Select(i => (i.StartUtc, i.EndUtc)).Should().Equal(
            (T("08:00"), T("09:00")), (T("09:00"), T("09:30")), (T("09:30"), T("10:00")));
        result.MergedIntervals[1].Operators.Should().BeEmpty();
    }

    [Fact]
    public void Different_operator_sets_are_not_merged()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "09:00"),
            Operator("A", 1, "09:00", "10:00"), Operator("B", 2, "09:00", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, Array.Empty<ShiftOutputCycle>(), OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().HaveCount(2);
        result.MergedIntervals[0].Operators.Select(o => o.OperatorId).Should().Equal("A");
        result.MergedIntervals[1].Operators.Select(o => o.OperatorId).Should().BeEquivalentTo("A", "B");
    }

    [Fact]
    public void Task_boundaries_do_not_split_merged_interval()
    {
        var windows = Windows((TaskId, "08:00", "09:00"), (OtherTaskId, "09:00", "10:00"));

        var result = ShiftOutputDistributor.Distribute(
            new[] { Operator("A", 1, "08:00", "10:00") }, Array.Empty<ShiftOutputCycle>(),
            windows, ShiftStart, FactEnd);

        result.MergedIntervals.Should().ContainSingle();
        result.Shares.Should().HaveCount(2);
        result.Shares.Single(s => s.TaskId == TaskId).StartUtc.Should().Be(T("08:00"));
        result.Shares.Single(s => s.TaskId == TaskId).EndUtc.Should().Be(T("09:00"));
        result.Shares.Single(s => s.TaskId == OtherTaskId).StartUtc.Should().Be(T("09:00"));
        result.Shares.Single(s => s.TaskId == OtherTaskId).EndUtc.Should().Be(T("10:00"));
    }

    [Fact]
    public void Cycles_for_two_tasks_are_grouped_separately()
    {
        var windows = Windows((TaskId, "08:00", "10:00"), (OtherTaskId, "08:00", "10:00"));
        var cycles = new[]
        {
            Cycle("09:00", 2), new ShiftOutputCycle(OtherTaskId, T("09:00"), 5)
        };

        var result = ShiftOutputDistributor.Distribute(
            new[] { Operator("A", 1, "08:00", "10:00") }, cycles, windows, ShiftStart, FactEnd);

        result.Shares.Single(s => s.TaskId == TaskId).Quantity.Should().Be(2);
        result.Shares.Single(s => s.TaskId == OtherTaskId).Quantity.Should().Be(5);
    }

    [Fact]
    public void Cycle_exactly_on_boundary_belongs_to_next_interval_and_shift_is_half_open()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "09:00"), Operator("B", 1, "09:00", "10:00")
        };
        var cycles = new[] { Cycle("08:00", 2), Cycle("09:00", 5), Cycle("10:00", 16) };

        var result = ShiftOutputDistributor.Distribute(
            assignments, cycles, OneTaskWindow(), ShiftStart, FactEnd);

        result.Shares.Single(s => s.OperatorId == "A").Quantity.Should().Be(2);
        result.Shares.Single(s => s.OperatorId == "B").Quantity.Should().Be(5);
        result.Shares.Sum(s => s.Quantity).Should().Be(7);
    }

    [Fact]
    public void Orphan_is_created_only_for_positive_output()
    {
        var empty = ShiftOutputDistributor.Distribute(
            Array.Empty<ShiftOperatorSlice>(), new[] { Cycle("08:30", 0) },
            OneTaskWindow(), ShiftStart, FactEnd);
        var positive = ShiftOutputDistributor.Distribute(
            Array.Empty<ShiftOperatorSlice>(), new[] { Cycle("08:30", 4) },
            OneTaskWindow(), ShiftStart, FactEnd);

        empty.MergedIntervals.Should().ContainSingle().Which.Operators.Should().BeEmpty();
        empty.Orphans.Should().BeEmpty();
        positive.Orphans.Should().ContainSingle().Which.Quantity.Should().Be(4);
    }

    [Fact]
    public void Leading_and_trailing_unstaffed_output_becomes_orphan()
    {
        var assignments = new[] { Operator("A", 1, "09:00", "09:30") };
        var cycles = new[] { Cycle("08:30", 2), Cycle("09:15", 3), Cycle("09:45", 5) };

        var result = ShiftOutputDistributor.Distribute(
            assignments, cycles, OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().HaveCount(3);
        result.Shares.Should().ContainSingle().Which.Quantity.Should().Be(3);
        result.Orphans.Select(o => (o.StartUtc, o.EndUtc, o.Quantity)).Should().Equal(
            (T("08:00"), T("09:00"), 2), (T("09:30"), T("10:00"), 5));
        result.Shares.Sum(s => s.Quantity).Should().Be(3);
        result.Orphans.Sum(o => o.Quantity).Should().Be(7);
    }

    [Fact]
    public void Assignments_are_clipped_to_shift_and_empty_clips_are_excluded()
    {
        var inside = Operator("C", 1, "07:00", "11:00");
        var assignments = new[]
        {
            Operator("A", 1, "07:00", "08:00"), Operator("B", 1, "10:00", "11:00"), inside
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, Array.Empty<ShiftOutputCycle>(), OneTaskWindow(), ShiftStart, FactEnd);

        var merged = result.MergedIntervals.Should().ContainSingle().Subject;
        merged.Operators.Should().ContainSingle().Which.AssignmentId.Should().Be(inside.AssignmentId);
        merged.Operators[0].StartUtc.Should().Be(ShiftStart);
        merged.Operators[0].EndUtc.Should().Be(FactEnd);
    }

    [Fact]
    public void Overlapping_rows_for_same_operator_produce_one_share()
    {
        var assignments = new[]
        {
            Operator("A", 1, "08:00", "10:00"), Operator("A", 1, "08:00", "10:00")
        };

        var result = ShiftOutputDistributor.Distribute(
            assignments, new[] { Cycle("09:00", 4) }, OneTaskWindow(), ShiftStart, FactEnd);

        result.MergedIntervals.Should().ContainSingle().Which.Operators.Should().ContainSingle();
        result.Shares.Should().ContainSingle().Which.Quantity.Should().Be(4);
    }

    [Fact]
    public void Shares_plus_orphans_conserve_output_for_multiple_layouts_and_quantities()
    {
        var layouts = new (string Name, IReadOnlyList<ShiftOperatorSlice> Assignments)[]
        {
            ("unstaffed", Array.Empty<ShiftOperatorSlice>()),
            ("single", new[] { Operator("A", 1, "07:00", "11:00") }),
            ("pair with slot swap", new[]
            {
                Operator("A", 1, "08:00", "09:00"), Operator("B", 2, "08:00", "09:00"),
                Operator("A", 2, "09:00", "10:00"), Operator("B", 1, "09:00", "10:00")
            }),
            ("changing sets with gap", new[]
            {
                Operator("A", 1, "08:00", "08:45"), Operator("B", 2, "08:00", "09:00"),
                Operator("A", 1, "09:30", "10:00")
            })
        };

        foreach (var layout in layouts)
        {
            for (var first = 0; first <= 5; first++)
            {
                for (var second = 0; second <= 5; second++)
                {
                    var cycles = new[] { Cycle("08:30", first), Cycle("09:30", second) };
                    var result = ShiftOutputDistributor.Distribute(
                        layout.Assignments, cycles, OneTaskWindow(), ShiftStart, FactEnd);

                    var distributed = result.Shares.Sum(s => s.Quantity) + result.Orphans.Sum(o => o.Quantity);
                    distributed.Should().Be(first + second,
                        $"layout '{layout.Name}' with quantities {first} and {second} must conserve output");
                }
            }
        }
    }

    private static ShiftOperatorSlice Operator(string operatorId, int slot, string start, string end) =>
        new(Guid.NewGuid(), operatorId, $"Operator {operatorId}", $"EMP-{operatorId}", slot, T(start), T(end));

    private static ShiftOutputCycle Cycle(string end, int quantity) => new(TaskId, T(end), quantity);

    private static IReadOnlyDictionary<Guid, Interval?> OneTaskWindow(
        string start = "08:00", string end = "10:00") =>
        Windows((TaskId, start, end));

    private static IReadOnlyDictionary<Guid, Interval?> Windows(
        params (Guid TaskId, string Start, string End)[] windows) =>
        windows.ToDictionary(w => w.TaskId, w => (Interval?)new Interval(T(w.Start), T(w.End)));

    private static DateTime T(string time) => Utc($"2026-11-18T{time}:00");

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
