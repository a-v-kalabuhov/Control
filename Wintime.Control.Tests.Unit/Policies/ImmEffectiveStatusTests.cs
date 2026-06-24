using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ImmEffectiveStatusTests
{
    // Матрица docs/details/Состояния_ТПА.xlsx, спроецированная на 6 эффективных состояний.
    // rawMode подаётся как ImmStatus (формат кеша, с заглавной) — Resolve нормализует сам.
    [Theory]
    // rawMode,   task,                        hasOpenDt, thresholdPassed, expected
    [InlineData("Auto",    ActiveTaskStatus.Setup,      false, false, EffectiveStatus.Setup)]
    [InlineData("Idle",    ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Setup)]
    [InlineData("Offline", ActiveTaskStatus.Setup,      false, true,  EffectiveStatus.Setup)]
    [InlineData("Auto",    ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)]
    [InlineData("Idle",    ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)] // до порога
    [InlineData("Manual",  ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Downtime)]   // порог пройден
    [InlineData("Alarm",   ActiveTaskStatus.InProgress, true,  false, EffectiveStatus.Downtime)]    // открыт простой
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, true,  EffectiveStatus.Downtime)]    // offline дольше порога
    [InlineData("Offline", ActiveTaskStatus.InProgress, false, false, EffectiveStatus.Production)]   // offline до порога (дребезг)
    [InlineData("Auto",    ActiveTaskStatus.None,        false, false, EffectiveStatus.Unplanned)]
    [InlineData("Offline", ActiveTaskStatus.None,        false, false, EffectiveStatus.Offline)]
    [InlineData("Idle",    ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    [InlineData("Manual",  ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    [InlineData("Alarm",   ActiveTaskStatus.None,        false, false, EffectiveStatus.NoTask)]
    public void Resolve_MatchesMatrix(
        string rawMode, ActiveTaskStatus task, bool hasOpenDowntime, bool thresholdPassed, string expected)
    {
        ImmEffectiveStatus.Resolve(rawMode, task, hasOpenDowntime, thresholdPassed)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_NormalizesMode_LowercaseAutoInProgress_Production()
    {
        ImmEffectiveStatus.Resolve("auto", ActiveTaskStatus.InProgress, false, false)
            .Should().Be(EffectiveStatus.Production);
    }
}
