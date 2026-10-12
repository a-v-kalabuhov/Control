using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Xunit;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Entities;

/// <summary>
/// BL-39: план, примечание, рабочий режим и эталоны цикла меняются только в «Планируется».
/// Неизменённое значение — no-op в любом статусе (форма в «Выдано» шлёт полный набор полей).
/// </summary>
public class ShiftTaskEditGuardsTests
{
    private const string LockedMessage = "Параметры задания можно изменить только в статусе «Планируется»";

    private static ShiftTask NewTask(EntityTaskStatus status) => new()
    {
        Status = status,
        PlanQuantity = 100,
        Note = "указание",
        WorkMode = WorkMode.Auto,
        PlannedFullCycleSeconds = 60,
        PlannedInjectionCycleSeconds = 20
    };

    public static TheoryData<EntityTaskStatus> NotDraft => new()
    {
        EntityTaskStatus.Issued, EntityTaskStatus.Setup, EntityTaskStatus.InProgress,
        EntityTaskStatus.Completed, EntityTaskStatus.Closed
    };

    // ── План ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetPlanQuantity_Draft_Changes()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetPlanQuantity(150);

        task.PlanQuantity.Should().Be(150);
    }

    [Theory]
    [MemberData(nameof(NotDraft))]
    public void SetPlanQuantity_ChangedOutsideDraft_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetPlanQuantity(150);

        act.Should().Throw<DomainException>().WithMessage(LockedMessage);
        task.PlanQuantity.Should().Be(100);
    }

    [Theory]
    [MemberData(nameof(NotDraft))]
    public void SetPlanQuantity_Unchanged_NoOpInAnyStatus(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetPlanQuantity(100);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void SetPlanQuantity_NotPositive_Throws(int quantity)
    {
        var task = NewTask(EntityTaskStatus.Draft);

        var act = () => task.SetPlanQuantity(quantity);

        act.Should().Throw<DomainException>().WithMessage("План должен быть больше нуля");
    }

    // ── Примечание ──────────────────────────────────────────────────────────

    [Fact]
    public void SetNote_Draft_Changes()
    {
        var task = NewTask(EntityTaskStatus.Draft);

        task.SetNote("новое");

        task.Note.Should().Be("новое");
    }

    [Theory]
    [MemberData(nameof(NotDraft))]
    public void SetNote_ChangedOutsideDraft_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetNote("новое");

        act.Should().Throw<DomainException>().WithMessage(LockedMessage);
    }

    [Fact]
    public void SetNote_Unchanged_NoOpOutsideDraft()
    {
        var task = NewTask(EntityTaskStatus.Issued);

        var act = () => task.SetNote("указание");

        act.Should().NotThrow();
    }

    [Fact]
    public void SetNote_EmptyStringForMissingNote_NoOpOutsideDraft()
    {
        // Форма подставляет '' вместо отсутствующего примечания — это не изменение.
        var task = NewTask(EntityTaskStatus.Issued);
        task.Note = null;

        var act = () => task.SetNote("");

        act.Should().NotThrow();
        task.Note.Should().BeNull();
    }

    // ── Режим и эталоны цикла ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotDraft))]
    public void SetCycleNorms_ChangedOutsideDraft_Throws(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetCycleNorms(WorkMode.SemiAuto, 60, 20, requireFullCycle: false);

        act.Should().Throw<DomainException>().WithMessage(LockedMessage);
        task.WorkMode.Should().Be(WorkMode.Auto);
    }

    [Theory]
    [MemberData(nameof(NotDraft))]
    public void SetCycleNorms_Unchanged_NoOpOutsideDraft(EntityTaskStatus status)
    {
        var task = NewTask(status);

        var act = () => task.SetCycleNorms(WorkMode.Auto, 60, 20, requireFullCycle: false);

        act.Should().NotThrow();
    }
}
