using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Reports;
using Xunit;

namespace Wintime.Control.Tests.Unit.Reports;

public class DailyStatusBreakdownTests
{
    private static readonly DateTime D0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime H(double h) => D0.AddHours(h);
    private static FactoryDay Day(double startH, double endH) =>
        new(DateTime.SpecifyKind(H(startH).Date, DateTimeKind.Unspecified), H(startH), H(endH));

    [Fact]
    public void Split_SegmentAcrossMidnight_IsDividedBetweenDays()
    {
        var segments = new[] { new EffectiveSegment(EffectiveStatus.Production, H(20), H(28)) };
        var days = new[] { Day(0, 24), Day(24, 48) };

        var result = DailyStatusBreakdown.Split(segments, days);

        result.Should().HaveCount(2);
        result[0][EffectiveStatus.Production].Should().Be(4 * 3600);
        result[0][EffectiveStatus.NoData].Should().Be(20 * 3600);
        result[1][EffectiveStatus.Production].Should().Be(4 * 3600);
        result[1][EffectiveStatus.NoData].Should().Be(20 * 3600);
    }

    [Fact]
    public void Split_UncoveredTailOfDay_IsNoData()
    {
        // Таймлайн обрезан «сейчас» = 10:00 — остаток суток неизвестен.
        var segments = new[] { new EffectiveSegment(EffectiveStatus.Setup, H(0), H(10)) };

        var result = DailyStatusBreakdown.Split(segments, new[] { Day(0, 24) });

        result[0][EffectiveStatus.Setup].Should().Be(10 * 3600);
        result[0][EffectiveStatus.NoData].Should().Be(14 * 3600);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void Split_SumOfDayEqualsDayLength(int dayHours)
    {
        var segments = new[]
        {
            new EffectiveSegment(EffectiveStatus.Offline,    H(0), H(5)),
            new EffectiveSegment(EffectiveStatus.Production, H(5), H(dayHours)),
        };

        var result = DailyStatusBreakdown.Split(segments, new[] { Day(0, dayHours) });

        result[0].Values.Sum().Should().Be(dayHours * 3600);
        result[0].Keys.Should().BeEquivalentTo(DailyStatusBreakdown.Keys);
    }

    [Fact]
    public void Split_NoSegments_WholeDayNoData()
    {
        var result = DailyStatusBreakdown.Split(System.Array.Empty<EffectiveSegment>(), new[] { Day(0, 24) });

        result[0][EffectiveStatus.NoData].Should().Be(86400);
    }

    [Fact]
    public void Efficiency_ProductionOverKnownTime()
    {
        var s = DailyStatusBreakdown.Empty();
        s[EffectiveStatus.Production] = 6 * 3600;
        s[EffectiveStatus.NoTask]     = 6 * 3600;  // снижает эффективность
        s[EffectiveStatus.Offline]    = 6 * 3600;  // не входит в знаменатель
        s[EffectiveStatus.NoData]     = 6 * 3600;  // не входит в знаменатель

        DailyStatusBreakdown.Efficiency(s).Should().Be(50m);
    }

    [Fact]
    public void Efficiency_NoKnownTime_IsNull()
    {
        var s = DailyStatusBreakdown.Empty();
        s[EffectiveStatus.Offline] = 3600;
        s[EffectiveStatus.NoData]  = 3600;

        DailyStatusBreakdown.Efficiency(s).Should().BeNull();
    }
}
