using Wintime.Control.Core.Policies;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Entities;

public class ShiftTask : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public Guid ProductId { get; set; }   // изделие задания (PZP-19); принадлежит типу ПФ задания
    public string? PersonnelId { get; set; } // Ссылка на User.Id
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public decimal ActualMaterialWeightGrams { get; set; }

    /// <summary>Вес одной детали, граммы. Копия веса изделия; правится менеджером в любом статусе (PZP-24).</summary>
    public decimal PartWeightGrams { get; set; }

    /// <summary>Тарная единица, штук. Копия умолчания типа изделия; правится менеджером в любом статусе (PZP-29).</summary>
    public int? PackagingUnit { get; set; }
    public Wintime.Control.Core.Enums.TaskStatus Status { get; set; } = Wintime.Control.Core.Enums.TaskStatus.Planned;
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

    /// <summary>
    /// Норматив наладки, минуты (PZP-14). Снапшот умолчания <see cref="PlanningDefaults"/>
    /// на момент создания; правится до начала наладки (дальше — мерка перерасхода, BL-12).
    /// </summary>
    public int PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фиксированная часть, минуты (PZP-14).</summary>
    public int ReleaseBufferFixedMinutes { get; set; }

    /// <summary>
    /// Буфер выпуска: процент от плановой длительности выпуска (без наладки), целое 0..100 (PZP-14).
    /// Буфер не хранится готовым — см. <see cref="TaskPlanCalculator"/>.
    /// </summary>
    public int ReleaseBufferPercent { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? SetupStartedAt { get; set; }
    public DateTime? MoldVerifiedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public Mold Mold { get; set; } = null!;
    public Product? Product { get; set; }
    public User? Personnel { get; set; }

    // Копия рецепта изделия на момент создания/смены изделия (BL-36).
    public List<ShiftTaskRecipeIngredient> RecipeIngredients { get; set; } = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Конечный автомат жизненного цикла задания:
    //   Planned → Issued → Setup → InProgress → Completed → Closed
    //                      └──── CancelSetup ────┘ (Setup → Issued)
    // Каждый переход проверяет исходный статус и кидает DomainException (→ HTTP 400).
    // Побочные эффекты (перевод режима ТПА через эмулятор) остаются в контроллере.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Выдать задание: Планируется (Planned) → Выдано (Issued); personnelId — назначить наладчика в той же операции. Без наладчика выдать нельзя (PZP-13).</summary>
    public void Issue(string? personnelId = null)
    {
        EnsureStatus(TaskStatus.Planned, "Задание не в статусе «Планируется»");
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
            case TaskStatus.Planned:
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

    /// <summary>Закрыть задание вручную (досрочно или после завершения): любой статус → Closed.</summary>
    public void Close(string? closeReason)
    {
        if (closeReason != null)
            CloseReason = closeReason;

        Status = TaskStatus.Closed;
        ClosedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// PZP-13: задать плановую дату и (необязательно) время начала наладки.
    /// Менять можно до начала наладки (Planned/Issued). Проверяется только изменённое значение:
    /// дата — не раньше сегодняшней, дата+время — не раньше «сейчас − 5 минут».
    /// </summary>
    /// <param name="nowLocal">Текущее время по часам завода (считает вызывающий).</param>
    public void SetPlannedStart(DateOnly date, TimeOnly? time, DateTime nowLocal)
    {
        bool dateChanged = date != PlannedDate;
        bool timeChanged = time != PlannedSetupStartTime;
        if (!dateChanged && !timeChanged)
            return;

        if (Status is not (TaskStatus.Planned or TaskStatus.Issued))
            throw new DomainException("Плановое начало можно изменить только до начала наладки");

        if (dateChanged && date < DateOnly.FromDateTime(nowLocal))
            throw new DomainException("Плановая дата не может быть в прошлом");

        if (time is TimeOnly t && date.ToDateTime(t) < nowLocal.AddMinutes(-5))
            throw new DomainException("Время начала наладки не может быть в прошлом");

        PlannedDate = date;
        PlannedSetupStartTime = time;
    }

    /// <summary>
    /// PZP-14: норматив наладки. Менять можно до начала наладки (Planned/Issued): после старта
    /// норматив — мерка перерасхода и задним числом не подкручивается. Неизменённое значение — no-op.
    /// </summary>
    public void SetSetupNorm(int minutes)
    {
        PlanningNormLimits.EnsureSetupMinutes(minutes);
        if (minutes == PlannedSetupMinutes)
            return;

        if (Status is not (TaskStatus.Planned or TaskStatus.Issued))
            throw new DomainException("Норматив наладки можно изменить только до начала наладки");

        PlannedSetupMinutes = minutes;
    }

    /// <summary>
    /// PZP-14: буфер выпуска (фикс. часть + процент). Менять можно до завершения выпуска —
    /// планировщик перепланирует хвост, если выпуск затягивается. Неизменённое значение — no-op.
    /// </summary>
    public void SetReleaseBuffer(int fixedMinutes, int percent)
    {
        PlanningNormLimits.EnsureBuffer(fixedMinutes, percent);
        if (fixedMinutes == ReleaseBufferFixedMinutes && percent == ReleaseBufferPercent)
            return;

        if (Status is TaskStatus.Completed or TaskStatus.Closed)
            throw new DomainException("Буфер выпуска можно изменить только до завершения выпуска");

        ReleaseBufferFixedMinutes = fixedMinutes;
        ReleaseBufferPercent = percent;
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

    /// <summary>PZP-24: изменить вес детали. Разрешено в любом статусе, включая архивные задания.</summary>
    public void SetPartWeight(decimal weightGrams)
    {
        if (weightGrams < 0)
            throw new DomainException("Вес детали должен быть неотрицательным");
        PartWeightGrams = weightGrams;
    }

    /// <summary>PZP-29: изменить или очистить тарную единицу. Разрешено в любом статусе.</summary>
    public void SetPackagingUnit(int? packagingUnit)
    {
        if (packagingUnit is <= 0)
            throw new DomainException("Тарная единица должна быть больше нуля");
        PackagingUnit = packagingUnit;
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
    /// PZP-19: сменить изделие. Только в «Планируется» и только без заказа (изделие задаёт заказ);
    /// неизменённое значение — no-op в любом статусе (форма шлёт поле при каждом сохранении).
    /// Возвращает <c>true</c>, если изделие реально сменилось: вызывающий по этому признаку
    /// перезаписывает копию рецепта (BL-36).
    /// Принадлежность изделия типу ПФ проверяет вызывающий код (ProductSelection).
    /// </summary>
    public bool SetProduct(Guid productId)
    {
        if (productId == ProductId)
            return false;

        EnsureEditable();
        if (OrderId != null)
            throw new DomainException("Изделие задания с заказом задаёт заказ");
        ProductId = productId;
        return true;
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

    // ─────────────────────────────────────────────────────────────────────────
    // Рецепт задания (BL-36): копия рецепта изделия, сделанная при создании или
    // смене изделия. Строки не добавляются и не удаляются вручную, процент не
    // правится — только замена материала на материал того же типа, в любом статусе.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// «Рецепт правился вручную» — хотя бы одна строка указывает не на исходный материал.
    /// Не хранится: замена туда и обратно правкой не считается.
    /// </summary>
    public bool RecipeEdited => RecipeIngredients.Any(i => i.MaterialId != i.OriginalMaterialId);

    /// <summary>
    /// BL-36: перезаписать копию рецептом изделия. Вызывается при создании задания
    /// и при реальной смене изделия; нет рецепта у изделия — копия пустая.
    /// Строки согласуются по текущему материалу: общие строки обновляются, а не пересоздаются,
    /// чтобы удаление и вставка не пересеклись на уникальном индексе (ShiftTaskId, MaterialId).
    /// Требует загруженной навигации <see cref="ProductRecipeIngredient.Material"/> —
    /// она же становится копией, поэтому И Include рецепта, и ThenInclude материала обязательны.
    /// </summary>
    public void ApplyRecipe(IEnumerable<ProductRecipeIngredient> productIngredients)
    {
        var rows = productIngredients.OrderBy(i => i.SortOrder).ToList();
        var keep = rows.Select(i => i.MaterialId).ToHashSet();
        RecipeIngredients.RemoveAll(i => !keep.Contains(i.MaterialId));

        for (var index = 0; index < rows.Count; index++)
        {
            var ingredient = rows[index];
            var material = ingredient.Material
                           ?? throw new InvalidOperationException("Материал рецепта изделия не загружен.");
            var existing = RecipeIngredients.FirstOrDefault(i => i.MaterialId == ingredient.MaterialId);
            if (existing == null)
            {
                RecipeIngredients.Add(new ShiftTaskRecipeIngredient
                {
                    MaterialId = material.Id,
                    Material = material,
                    OriginalMaterialId = material.Id,
                    OriginalMaterial = material,
                    Percent = ingredient.Percent,
                    SortOrder = index
                });
            }
            else
            {
                existing.MaterialId = material.Id;
                existing.Material = material;
                existing.OriginalMaterialId = material.Id;
                existing.OriginalMaterial = material;
                existing.Percent = ingredient.Percent;
                existing.SortOrder = index;
            }
        }
    }

    /// <summary>
    /// BL-36: заменить материал строки копии. Только материал того же типа и активный;
    /// статус задания не проверяется — замена разрешена в любом статусе (даже после закрытия).
    /// Выбор исходного материала возвращает строку к исходному состоянию.
    /// Требует загруженной навигации <see cref="ShiftTaskRecipeIngredient.Material"/>.
    /// </summary>
    public void ReplaceIngredient(Guid ingredientId, Material newMaterial)
    {
        var row = RecipeIngredients.FirstOrDefault(i => i.Id == ingredientId)
                  ?? throw new DomainException("Строка рецепта не найдена.");
        var current = row.Material
                      ?? throw new InvalidOperationException("Материал строки рецепта не загружен.");

        if (!newMaterial.IsActive)
            throw new DomainException("Архивный материал нельзя выбрать ингредиентом рецепта.");
        if (newMaterial.Type != current.Type)
            throw new DomainException(
                $"Можно выбрать только {MaterialRules.TypeLabel(current.Type)} — как у текущего ингредиента.");
        if (row.MaterialId != newMaterial.Id &&
            RecipeIngredients.Any(i => i.Id != row.Id && i.MaterialId == newMaterial.Id))
            throw new DomainException("Материал уже используется в другой строке рецепта задания.");

        row.MaterialId = newMaterial.Id;
        row.Material = newMaterial;
    }

    /// <summary>
    /// Параметры задания (план, примечание, режим, эталоны) меняются только в «Планируется»:
    /// после выдачи наладчик работает по ним, после начала — они часть факта (BL-39).
    /// </summary>
    private void EnsureEditable() =>
        EnsureStatus(TaskStatus.Planned, "Параметры задания можно изменить только в статусе «Планируется»");

    private void EnsureStatus(TaskStatus expected, string message)
    {
        if (Status != expected)
            throw new DomainException(message);
    }
}
