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

    // PZP-05: связь с заказом (nullable — задание может быть без заказа) + ручной брак.
    public Guid? OrderId { get; set; }
    public Order? Order { get; set; }
    public int DefectQuantity { get; set; }   // брак (ручной ввод при завершении), default 0

    // Рабочий режим и эталонные длительности цикла (спека 2026-08-01).
    // В будущем переезжают в технологическую карту.
    public WorkMode WorkMode { get; set; } = WorkMode.Auto;

    /// <summary>
    /// Эталонная длительность полного цикла, секунды. Обязателен для новых заданий
    /// (менеджер вычисляет его при планировании). <c>null</c> — legacy-задание,
    /// созданное до появления поля.
    /// </summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>
    /// Эталонная длительность цикла литья, секунды. Необязателен: технологический
    /// параметр, нужен только для анализа стабильности.
    /// </summary>
    public int? PlannedInjectionCycleSeconds { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Плановая дата выполнения — календарная дата завода (PZP-13). Обязательна.</summary>
    public DateOnly PlannedDate { get; set; }

    /// <summary>
    /// Плановое время начала наладки по часам завода (PZP-13). Необязательно:
    /// <c>null</c> — задание начинается с начала первой смены дня (см. PlannedTaskStart).
    /// </summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }
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

    /// <summary>Выдать задание: Планируется (Draft) → Выдано (Issued); personnelId — назначить наладчика в той же операции. Без наладчика выдать нельзя (PZP-13).</summary>
    public void Issue(string? personnelId = null)
    {
        EnsureStatus(TaskStatus.Draft, "Задание не в статусе «Планируется»");
        if (!string.IsNullOrWhiteSpace(personnelId))
            PersonnelId = personnelId;
        if (string.IsNullOrEmpty(PersonnelId))
            throw new DomainException("Назначьте наладчика");
        Status = TaskStatus.Issued;
        IssuedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// PZP-13: назначить/снять наладчика. «Планируется» — можно снять; «Выдано» — только
    /// переназначить (снять нельзя); после начала наладки — запрещено.
    /// Существование и активность наладчика проверяет вызывающий.
    /// </summary>
    public void AssignPersonnel(string? personnelId)
    {
        var id = string.IsNullOrWhiteSpace(personnelId) ? null : personnelId;
        switch (Status)
        {
            case TaskStatus.Draft:
                break;
            case TaskStatus.Issued when id is null:
                throw new DomainException("У выданного задания должен быть наладчик");
            case TaskStatus.Issued:
                break;
            default:
                throw new DomainException("Наладчика можно изменить только до начала наладки");
        }
        PersonnelId = id;
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
    /// <param name="defectQuantity">Брак (PZP-05); валидируется диапазоном 0..выпущено.</param>
    public void Complete(int? actualQuantity, string? completionReason, int? defectQuantity = null)
    {
        EnsureStatus(TaskStatus.InProgress, "Задание не в работе");

        if (actualQuantity.HasValue)
            ActualQuantity = actualQuantity.Value;

        if (defectQuantity.HasValue)
        {
            if (defectQuantity.Value < 0 || defectQuantity.Value > ActualQuantity)
                throw new DomainException("Брак должен быть в диапазоне 0..выпущено");
            DefectQuantity = defectQuantity.Value;
        }

        if (ActualQuantity != PlanQuantity && completionReason != null)
            CloseReason = completionReason;

        Status = TaskStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// PZP-08: увеличить план задания в работе (компенсация брака / допвыпуск).
    /// Только в статусе InProgress; не меняет статус. Прибавляет к текущему плану.
    /// </summary>
    /// <param name="delta">Дополнительное количество; должно быть больше нуля.</param>
    public void AddPlannedQuantity(int delta)
    {
        EnsureStatus(TaskStatus.InProgress, "Задание не в работе");
        if (delta <= 0)
            throw new DomainException("Дополнительное количество должно быть больше нуля");
        PlanQuantity += delta;
    }

    /// <summary>Закрыть задание (ручное закрытие в конце дня): любой статус → Closed.</summary>
    public void Close(string? closeReason)
    {
        if (closeReason != null)
            CloseReason = closeReason;

        Status = TaskStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// PZP-13: задать плановую дату и (необязательно) время начала наладки.
    /// Менять можно до начала наладки (Draft/Issued). Проверяется только изменённое значение:
    /// дата — не раньше сегодняшней, дата+время — не раньше «сейчас − 5 минут».
    /// </summary>
    /// <param name="nowLocal">Текущее время по часам завода (считает вызывающий).</param>
    public void SetPlannedStart(DateOnly date, TimeOnly? time, DateTime nowLocal)
    {
        bool dateChanged = date != PlannedDate;
        bool timeChanged = time != PlannedSetupStartTime;
        if (!dateChanged && !timeChanged)
            return;

        if (Status is not (TaskStatus.Draft or TaskStatus.Issued))
            throw new DomainException("Плановое начало можно изменить только до начала наладки");

        if (dateChanged && date < DateOnly.FromDateTime(nowLocal))
            throw new DomainException("Плановая дата не может быть в прошлом");

        if (time is TimeOnly t && date.ToDateTime(t) < nowLocal.AddMinutes(-5))
            throw new DomainException("Время начала наладки не может быть в прошлом");

        PlannedDate = date;
        PlannedSetupStartTime = time;
    }

    /// <summary>
    /// Задать рабочий режим и эталонные длительности цикла.
    /// </summary>
    /// <param name="requireFullCycle">
    /// <c>true</c> при создании задания — эталон полного цикла обязателен.
    /// <c>false</c> при редактировании — legacy-задание можно сохранить без него.
    /// </param>
    public void SetCycleNorms(WorkMode workMode, int? fullCycleSeconds,
                              int? injectionCycleSeconds, bool requireFullCycle)
    {
        if (requireFullCycle && fullCycleSeconds is null)
            throw new DomainException("Не задан эталон полного цикла");

        if (fullCycleSeconds is <= 0)
            throw new DomainException("Эталон полного цикла должен быть больше нуля");

        if (injectionCycleSeconds is <= 0)
            throw new DomainException("Эталон цикла литья должен быть больше нуля");

        if (injectionCycleSeconds is not null)
        {
            if (fullCycleSeconds is null)
                throw new DomainException("Нельзя задать эталон цикла литья без эталона полного цикла");
            if (injectionCycleSeconds > fullCycleSeconds)
                throw new DomainException("Эталон цикла литья не может превышать эталон полного цикла");
        }

        if (workMode == WorkMode
            && fullCycleSeconds == PlannedFullCycleSeconds
            && injectionCycleSeconds == PlannedInjectionCycleSeconds)
            return;

        EnsureEditable();

        WorkMode = workMode;
        PlannedFullCycleSeconds = fullCycleSeconds;
        PlannedInjectionCycleSeconds = injectionCycleSeconds;
    }

    /// <summary>
    /// BL-39: изменить план (шт.). Только в «Планируется»; неизменённое значение — no-op в любом статусе.
    /// План задания в работе увеличивается только через <see cref="AddPlannedQuantity"/> (PZP-08).
    /// </summary>
    public void SetPlanQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("План должен быть больше нуля");
        if (quantity == PlanQuantity)
            return;

        EnsureEditable();
        PlanQuantity = quantity;
    }

    /// <summary>
    /// BL-39: изменить примечание. Только в «Планируется»; неизменённое значение — no-op.
    /// Пустая строка = нет примечания (форма подставляет '' вместо null).
    /// </summary>
    public void SetNote(string? note)
    {
        if (string.IsNullOrEmpty(note))
            note = null;
        if (note == Note || (note is null && string.IsNullOrEmpty(Note)))
            return;

        EnsureEditable();
        Note = note;
    }

    /// <summary>
    /// Параметры задания (план, примечание, режим, эталоны) меняются только в «Планируется»:
    /// после выдачи наладчик работает по ним, после начала — они часть факта (BL-39).
    /// </summary>
    private void EnsureEditable() =>
        EnsureStatus(TaskStatus.Draft, "Параметры задания можно изменить только в статусе «Планируется»");

    private void EnsureStatus(TaskStatus expected, string message)
    {
        if (Status != expected)
            throw new DomainException(message);
    }
}
