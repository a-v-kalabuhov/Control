using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

/// <summary>
/// Тесты конечного автомата задания, перенесённого из контроллера в сущность:
/// Draft → Issued → Setup → InProgress → Completed → Closed.
/// </summary>
public class ShiftTaskStateMachineTests
{
    private static ShiftTask NewTask(EntityTaskStatus status, int plan = 100) =>
        new() { Status = status, PlanQuantity = plan };

    // ── Успешные переходы ────────────────────────────────────────────────────

    [Fact]
    public void Issue_FromDraft_MovesToIssued()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.Issue();

        task.Status.Should().Be(EntityTaskStatus.Issued);
        task.IssuedAt.Should().NotBeNull();
    }

    [Fact]
    public void StartSetup_FromIssued_MovesToSetup()
    {
        var task = NewTask(EntityTaskStatus.Issued);

        task.StartSetup();

        task.Status.Should().Be(EntityTaskStatus.Setup);
        task.SetupStartedAt.Should().NotBeNull();
    }

    [Fact]
    public void CompleteSetup_FromSetup_MovesToInProgress()
    {
        var task = NewTask(EntityTaskStatus.Setup);

        task.CompleteSetup();

        task.Status.Should().Be(EntityTaskStatus.InProgress);
        task.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void CancelSetup_FromSetup_ReturnsToIssuedAndResetsMarks()
    {
        var task = NewTask(EntityTaskStatus.Setup);
        task.SetupStartedAt = DateTime.UtcNow;
        task.MoldVerifiedAt = DateTime.UtcNow;

        task.CancelSetup();

        task.Status.Should().Be(EntityTaskStatus.Issued);
        task.SetupStartedAt.Should().BeNull();
        task.MoldVerifiedAt.Should().BeNull();
    }

    [Fact]
    public void VerifyMold_FromSetup_SetsMarkWithoutChangingStatus()
    {
        var task = NewTask(EntityTaskStatus.Setup);

        task.VerifyMold();

        task.Status.Should().Be(EntityTaskStatus.Setup);
        task.MoldVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public void Complete_FromInProgress_MovesToCompleted()
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        task.Complete(actualQuantity: 100, completionReason: null);

        task.Status.Should().Be(EntityTaskStatus.Completed);
        task.ActualQuantity.Should().Be(100);
        task.CompletedAt.Should().NotBeNull();
        task.CloseReason.Should().BeNull();
    }

    [Fact]
    public void Complete_WithShortfall_RecordsCompletionReason()
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        task.Complete(actualQuantity: 80, completionReason: "Брак пресс-формы");

        task.CloseReason.Should().Be("Брак пресс-формы");
    }

    [Fact]
    public void Complete_OnPlan_IgnoresCompletionReason()
    {
        var task = NewTask(EntityTaskStatus.InProgress, plan: 100);

        task.Complete(actualQuantity: 100, completionReason: "не должно записаться");

        task.CloseReason.Should().BeNull();
    }

    [Fact]
    public void Close_FromAnyStatus_MovesToClosed()
    {
        var task = NewTask(EntityTaskStatus.Completed);

        task.Close(closeReason: "Конец смены");

        task.Status.Should().Be(EntityTaskStatus.Closed);
        task.ClosedAt.Should().NotBeNull();
        task.CloseReason.Should().Be("Конец смены");
    }

    // ── Недопустимые переходы → DomainException ──────────────────────────────

    [Theory]
    [InlineData(EntityTaskStatus.Issued)]
    [InlineData(EntityTaskStatus.Setup)]
    [InlineData(EntityTaskStatus.InProgress)]
    [InlineData(EntityTaskStatus.Completed)]
    [InlineData(EntityTaskStatus.Closed)]
    public void Issue_FromNonDraft_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.Issue();

        act.Should().Throw<DomainException>();
        task.Status.Should().Be(status); // статус не изменился
    }

    [Fact]
    public void StartSetup_FromDraft_Throws()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.StartSetup();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CompleteSetup_FromIssued_Throws()
    {
        var task = NewTask(EntityTaskStatus.Issued);

        var act = () => task.CompleteSetup();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Complete_FromSetup_Throws()
    {
        var task = NewTask(EntityTaskStatus.Setup);

        var act = () => task.Complete(actualQuantity: 100, completionReason: null);

        act.Should().Throw<DomainException>();
    }
}
