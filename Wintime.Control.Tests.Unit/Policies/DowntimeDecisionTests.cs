using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class DowntimeDecisionTests
{
    private static readonly DateTime Now = new(2026, 6, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Started = new(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NonAuto_InProgress_PastThreshold_NoOpen_OpensAtStatusSince()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: false, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Open);
        r.At.Should().Be(since); // бэкдейт на начало не-Auto
    }

    [Fact]
    public void NonAuto_InProgress_BeforeThreshold_None()
    {
        var since = Now.AddSeconds(-30);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void Open_StartTime_ClampedToTaskStarted_WhenStatusOlderThanTask()
    {
        // Статус ушёл в idle ещё до запуска задания (во время наладки) —
        // начало простоя не должно залезать раньше StartedAt.
        var since = Started.AddSeconds(-300);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.Open);
        r.At.Should().Be(Started);
    }

    [Fact]
    public void Offline_InProgress_PastThreshold_Opens()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Offline, since, Now, ActiveTaskStatus.InProgress, Started,
            false, 120);

        r.Action.Should().Be(DowntimeAction.Open);
    }

    [Fact]
    public void Auto_WithOpenAutoDowntime_Closes_AtStatusSince()
    {
        var since = Now.AddSeconds(-10);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Auto, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Close);
        r.At.Should().Be(since);
    }

    [Fact]
    public void TaskLeftInProgress_WithOpenAutoDowntime_Closes()
    {
        var since = Now.AddSeconds(-10);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.None, taskStartedAtUtc: null,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.Close);
    }

    [Fact]
    public void NonAuto_PastThreshold_AlreadyOpen_None()
    {
        var since = Now.AddSeconds(-200);
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, since, Now, ActiveTaskStatus.InProgress, Started,
            hasOpenAutoDowntime: true, thresholdSeconds: 120);

        r.Action.Should().Be(DowntimeAction.None);
    }

    [Fact]
    public void NoTask_NoOpen_None()
    {
        var r = DowntimeDecision.Evaluate(
            ImmStatus.Idle, Now.AddSeconds(-500), Now, ActiveTaskStatus.None, null,
            false, 120);

        r.Action.Should().Be(DowntimeAction.None);
    }
}
