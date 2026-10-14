using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class CyclePauseTests
{
    private static readonly DateTime PrevEnd = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
    private const int Threshold = 900;

    [Fact]
    public void Resolve_ShortPauseInAuto_ReturnsMilliseconds()
    {
        // залипание формы / ожидание оператора: 2 мин, статус всё время Auto
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(2), foreignStatusInPause: false, Threshold)
            .Should().Be(120_000);
    }

    [Fact]
    public void Resolve_SubSecondPause_KeepsMilliseconds()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMilliseconds(1500), false, Threshold).Should().Be(1500);
    }

    [Fact]
    public void Resolve_ZeroPause_ReturnsZero()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd, false, Threshold).Should().Be(0);
    }

    [Fact]
    public void Resolve_NoPreviousCycle_ReturnsNull()
    {
        CyclePause.Resolve(null, PrevEnd, false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_ForeignStatusInPause_ReturnsNull()
    {
        // короткая ручная наладка (Manual) / простой (Idle) / авария
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(5), foreignStatusInPause: true, Threshold)
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_PauseAtThreshold_ReturnsNull()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(Threshold), false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_PauseJustBelowThreshold_ReturnsValue()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(Threshold - 1), false, Threshold)
            .Should().Be((Threshold - 1) * 1000);
    }

    [Fact]
    public void Resolve_LongOfflineOnly_ReturnsNullByThreshold()
    {
        // долгий обрыв связи: Offline не «чужой» статус, но пауза ≥ порога
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(45), false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_NegativePause_ReturnsNull()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(-1), false, Threshold).Should().BeNull();
    }

    [Theory]
    [InlineData(ImmStatus.Auto, true)]
    [InlineData(ImmStatus.Offline, true)]
    [InlineData(ImmStatus.Manual, false)]
    [InlineData(ImmStatus.Idle, false)]
    [InlineData(ImmStatus.Alarm, false)]
    public void IsPauseStatus_OnlyAutoAndOffline(string status, bool expected)
    {
        CyclePause.IsPauseStatus(status).Should().Be(expected);
    }
}
