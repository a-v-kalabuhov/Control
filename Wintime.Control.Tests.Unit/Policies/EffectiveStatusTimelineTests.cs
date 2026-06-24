using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class EffectiveStatusTimelineTests
{
    private static readonly DateTime T0 = new(2026, 6, 24, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime M(int min) => T0.AddMinutes(min);

    [Fact]
    public void Build_SetupThenProduction_NoGap()
    {
        // Наладка 0–30 (auto), затем работа 30–60 (auto). Простоев нет.
        var raw = new[] { new RawSegment(ImmStatus.Auto, M(0), M(60)) };
        var tasks = new[]
        {
            new TaskInterval(ActiveTaskStatus.Setup,      M(0),  M(30)),
            new TaskInterval(ActiveTaskStatus.InProgress, M(30), M(60)),
        };
        var result = EffectiveStatusTimeline.Build(raw, tasks, System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Setup, M(0), M(30)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(30), M(60)));
    }

    [Fact]
    public void Build_NonAutoBeforeDowntime_IsProduction_CoveredByEvent_IsDowntime()
    {
        // Работа всё время InProgress. Сырой: auto 0–20, idle 20–60.
        // Простой зафиксирован Event только 40–60 (порог пройден к 40-й минуте).
        var raw = new[]
        {
            new RawSegment(ImmStatus.Auto, M(0),  M(20)),
            new RawSegment(ImmStatus.Idle, M(20), M(60)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(0), M(60)) };
        var downtimes = new[] { new Interval(M(40), M(60)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, downtimes, M(0), M(60));

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(0), M(40)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Downtime, M(40), M(60)));
    }

    [Fact]
    public void Build_ClampsToWindow_AndMergesAdjacentEqual()
    {
        // Сырой статус выходит за окно; два смежных auto-InProgress сегмента должны слиться.
        var raw = new[]
        {
            new RawSegment(ImmStatus.Auto, M(-30), M(20)),
            new RawSegment(ImmStatus.Auto, M(20),  M(90)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(-30), M(90)) };

        var result = EffectiveStatusTimeline.Build(raw, tasks, System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().ContainSingle();
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(0), M(60)));
    }

    [Fact]
    public void Build_NoTaskOffline_GivesOffline()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, M(0), M(60)) };
        var result = EffectiveStatusTimeline.Build(
            raw, System.Array.Empty<TaskInterval>(), System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().ContainSingle();
        result[0].EffectiveStatus.Should().Be(EffectiveStatus.Offline);
    }
}
