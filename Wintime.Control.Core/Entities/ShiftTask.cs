using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Entities;

public class ShiftTask : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public string? PersonnelId { get; set; } // Ссылка на User.Id
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public decimal ActualMaterialWeightGrams { get; set; }
    public Wintime.Control.Core.Enums.TaskStatus Status { get; set; } = Wintime.Control.Core.Enums.TaskStatus.Draft;
    public string? Note { get; set; }
    public string? CloseReason { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? PlannedDate { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? SetupStartedAt { get; set; }
    public DateTime? MoldVerifiedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public Mold Mold { get; set; } = null!;
    public User? Personnel { get; set; }

    // ─────────────────────────────────────────────────────────────────────────
    // Конечный автомат жизненного цикла задания:
    //   Draft → Issued → Setup → InProgress → Completed → Closed
    //                      └──── CancelSetup ────┘ (Setup → Issued)
    // Каждый переход проверяет исходный статус и кидает DomainException (→ HTTP 400).
    // Побочные эффекты (перевод режима ТПА через эмулятор) остаются в контроллере.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Выдать задание: Draft → Issued.</summary>
    public void Issue()
    {
        EnsureStatus(TaskStatus.Draft, "Задание не является черновиком");
        Status = TaskStatus.Issued;
        IssuedAt = DateTime.UtcNow;
    }

    /// <summary>Начать наладку: Issued → Setup.</summary>
    public void StartSetup()
    {
        EnsureStatus(TaskStatus.Issued, "Задание не в статусе «Выдано»");
        Status = TaskStatus.Setup;
        SetupStartedAt = DateTime.UtcNow;
    }

    /// <summary>Завершить наладку: Setup → InProgress.</summary>
    public void CompleteSetup()
    {
        EnsureStatus(TaskStatus.Setup, "Задание не в статусе «Наладка»");
        Status = TaskStatus.InProgress;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>Отменить наладку: Setup → Issued (сбрасывает метки наладки).</summary>
    public void CancelSetup()
    {
        EnsureStatus(TaskStatus.Setup, "Задание не в статусе «Наладка»");
        Status = TaskStatus.Issued;
        SetupStartedAt = null;
        MoldVerifiedAt = null;
    }

    /// <summary>Зафиксировать верификацию ПФ по QR (только в статусе Setup, без смены статуса).</summary>
    public void VerifyMold()
    {
        EnsureStatus(TaskStatus.Setup, "Задание не в статусе «Наладка»");
        MoldVerifiedAt = DateTime.UtcNow;
    }

    /// <summary>Завершить задание: InProgress → Completed.</summary>
    /// <param name="actualQuantity">Фактический выпуск; если null — остаётся накопленное значение.</param>
    /// <param name="completionReason">Причина отклонения; фиксируется только при расхождении с планом.</param>
    public void Complete(int? actualQuantity, string? completionReason)
    {
        EnsureStatus(TaskStatus.InProgress, "Задание не в работе");

        if (actualQuantity.HasValue)
            ActualQuantity = actualQuantity.Value;

        if (ActualQuantity != PlanQuantity && completionReason != null)
            CloseReason = completionReason;

        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>Закрыть задание (ручное закрытие в конце дня): любой статус → Closed.</summary>
    public void Close(string? closeReason)
    {
        if (closeReason != null)
            CloseReason = closeReason;

        Status = TaskStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    private void EnsureStatus(TaskStatus expected, string message)
    {
        if (Status != expected)
            throw new DomainException(message);
    }
}
