using FluentAssertions;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class TelemetryPeriodCalculatorTests
{
    private static readonly DateTime Now = new(2026, 11, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Current_month_and_week_end_at_now_and_expose_calendar_end_dates()
    {
        var result = TelemetryPeriodCalculator.CreateStatisticsPeriods(Now, "Europe/Moscow");

        result.CurrentMonth.StartDate.Should().Be(new DateOnly(2026, 11, 1));
        result.CurrentMonth.EndDate.Should().Be(new DateOnly(2026, 11, 30));
        result.CurrentMonth.StartUtc.Should().Be(new DateTime(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc));
        result.CurrentMonth.EndUtc.Should().Be(Now);
        result.CurrentMonth.IsCurrent.Should().BeTrue();

        result.RecentWeeks.Should().HaveCount(4);
        result.RecentWeeks[^1].StartDate.Should().Be(new DateOnly(2026, 11, 23));
        result.RecentWeeks[^1].EndDate.Should().Be(new DateOnly(2026, 11, 29));
        result.RecentWeeks[^1].StartUtc.Should().Be(new DateTime(2026, 11, 22, 21, 0, 0, DateTimeKind.Utc));
        result.RecentWeeks[^1].EndUtc.Should().Be(Now);
        result.RecentWeeks[^1].IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void Weeks_and_previous_months_cross_year_and_are_ordered_oldest_first()
    {
        var now = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

        var result = TelemetryPeriodCalculator.CreateStatisticsPeriods(now, "Europe/Moscow");

        result.RecentWeeks.Select(w => w.StartDate).Should().Equal(
            new DateOnly(2025, 12, 15), new DateOnly(2025, 12, 22),
            new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 5));
        result.RecentWeeks.Take(3).Should().OnlyContain(w => !w.IsCurrent);
        result.RecentWeeks[0].EndUtc.Should().Be(result.RecentWeeks[1].StartUtc);
        result.PreviousMonths.Select(m => m.StartDate).Should().Equal(
            new DateOnly(2025, 10, 1), new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1));
        result.PreviousMonths.Should().OnlyContain(m => !m.IsCurrent);
        result.PreviousMonths[^1].EndUtc.Should().Be(result.CurrentMonth.StartUtc);
    }

    [Fact]
    public void Previous_months_include_leap_february()
    {
        var now = new DateTime(2028, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        var result = TelemetryPeriodCalculator.CreateStatisticsPeriods(now, "Europe/Moscow");

        result.PreviousMonths[^1].StartDate.Should().Be(new DateOnly(2028, 2, 1));
        result.PreviousMonths[^1].EndDate.Should().Be(new DateOnly(2028, 2, 29));
        result.PreviousMonths[^1].EndUtc.Should().Be(new DateTime(2028, 2, 29, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Local_midnight_is_converted_to_utc_with_utc_kind()
    {
        var cutoff = TelemetryPeriodCalculator.GetCleanupCutoffUtc(
            new DateOnly(2026, 11, 27), Now, "Europe/Moscow");

        cutoff.Should().Be(new DateTime(2026, 11, 26, 21, 0, 0, DateTimeKind.Utc));
        cutoff.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Week_windows_follow_spring_dst_transition()
    {
        var now = new DateTime(2026, 3, 11, 12, 0, 0, DateTimeKind.Utc);

        var result = TelemetryPeriodCalculator.CreateStatisticsPeriods(now, "America/New_York");
        var transitionWeek = result.RecentWeeks.Single(w => w.StartDate == new DateOnly(2026, 3, 2));

        transitionWeek.StartUtc.Should().Be(new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc));
        transitionWeek.EndUtc.Should().Be(new DateTime(2026, 3, 9, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Week_windows_follow_autumn_dst_transition()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var result = TelemetryPeriodCalculator.CreateStatisticsPeriods(now, "America/New_York");
        var transitionWeek = result.RecentWeeks.Single(w => w.StartDate == new DateOnly(2026, 10, 26));

        transitionWeek.StartUtc.Should().Be(new DateTime(2026, 10, 26, 4, 0, 0, DateTimeKind.Utc));
        transitionWeek.EndUtc.Should().Be(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Today_is_allowed_and_future_date_is_rejected_in_browser_timezone()
    {
        var today = new DateOnly(2026, 11, 27);

        var allowed = TelemetryPeriodCalculator.GetCleanupCutoffUtc(today, Now, "Europe/Moscow");
        var act = () => TelemetryPeriodCalculator.GetCleanupCutoffUtc(today.AddDays(1), Now, "Europe/Moscow");

        allowed.Should().Be(new DateTime(2026, 11, 26, 21, 0, 0, DateTimeKind.Utc));
        act.Should().Throw<DomainException>().WithMessage("*будущ*");
    }

    [Fact]
    public void Same_preview_and_cleanup_inputs_produce_same_cutoff()
    {
        var date = new DateOnly(2026, 11, 20);

        var preview = TelemetryPeriodCalculator.GetCleanupCutoffUtc(date, Now, "Europe/Moscow");
        var cleanup = TelemetryPeriodCalculator.GetCleanupCutoffUtc(date, Now, "Europe/Moscow");

        cleanup.Should().Be(preview);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Real_Zone")]
    public void Missing_or_unknown_timezone_is_domain_error(string? zone)
    {
        var act = () => TelemetryPeriodCalculator.CreateStatisticsPeriods(Now, zone);

        act.Should().Throw<DomainException>().WithMessage("*часовой пояс*");
    }
}
