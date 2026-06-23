using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class CycleProcessingPolicyTests
{
    // Строки из docs/details/Состояния_ТПА.xlsx.
    // command → (task, hasOpenDowntime): Наладка→Setup; Работа→InProgress,false;
    // Простой(ручной)→InProgress,true; "-"→None,false.
    [Theory]
    // signal, task, hasOpenDowntime, expectedCycle, expectedOutput
    [InlineData("idle",   ActiveTaskStatus.Setup,      false, false, false)] // 1
    [InlineData("idle",   ActiveTaskStatus.InProgress, false, true,  false)] // 2
    [InlineData("idle",   ActiveTaskStatus.InProgress, true,  true,  false)] // 3
    [InlineData("idle",   ActiveTaskStatus.None,       false, false, false)] // 3а
    [InlineData("alarm",  ActiveTaskStatus.Setup,      false, false, false)] // 4
    [InlineData("alarm",  ActiveTaskStatus.InProgress, false, true,  false)] // 5
    [InlineData("alarm",  ActiveTaskStatus.InProgress, true,  true,  false)] // 6
    [InlineData("alarm",  ActiveTaskStatus.None,       false, false, false)] // alarm,-
    [InlineData("manual", ActiveTaskStatus.Setup,      false, false, false)] // 7
    [InlineData("manual", ActiveTaskStatus.InProgress, false, true,  false)] // 8
    [InlineData("manual", ActiveTaskStatus.InProgress, true,  true,  false)] // 9
    [InlineData("manual", ActiveTaskStatus.None,       false, false, false)] // 9а
    [InlineData("auto",   ActiveTaskStatus.Setup,      false, false, false)] // 10
    [InlineData("auto",   ActiveTaskStatus.InProgress, false, true,  true )] // 11 ← единственный выпуск
    [InlineData("auto",   ActiveTaskStatus.InProgress, true,  true,  false)] // 12
    [InlineData("auto",   ActiveTaskStatus.None,       false, true,  false)] // 12а
    public void Matrix_MatchesSpecDocument(
        string signal, ActiveTaskStatus task, bool hasOpenDowntime,
        bool expectedCycle, bool expectedOutput)
    {
        CycleProcessingPolicy.ShouldProcessCycle(signal, task)
            .Should().Be(expectedCycle);
        CycleProcessingPolicy.ShouldCountOutput(signal, task, hasOpenDowntime)
            .Should().Be(expectedOutput);
    }

    [Fact]
    public void ShouldProcessCycle_NormalizesMode_UppercaseAutoNoTask_True()
    {
        CycleProcessingPolicy.ShouldProcessCycle("AUTO", ActiveTaskStatus.None)
            .Should().BeTrue();
    }
}
