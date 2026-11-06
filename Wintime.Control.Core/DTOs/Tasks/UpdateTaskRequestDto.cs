using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class UpdateTaskRequestDto
{
    public int? PlanQuantity { get; set; }
    /// <summary>Гнёзда задания; null — не менять. Доступно в любом статусе (PZP-36).</summary>
    public int? Cavities { get; set; }
    /// <summary>Вес детали, г; null — не менять. Доступен в любом статусе (PZP-24).</summary>
    public decimal? PartWeightGrams { get; set; }
    /// <summary>Тарная единица, шт.; null — не менять. Доступна в любом статусе (PZP-29).</summary>
    public int? PackagingUnit { get; set; }
    /// <summary>Явно очистить тарную единицу.</summary>
    public bool ClearPackagingUnit { get; set; }
    /// <summary>Изделие задания (PZP-19); <c>null</c> — не менять.</summary>
    public Guid? ProductId { get; set; }
    public string? Note { get; set; }
    /// <summary>Плановая дата; <c>null</c> — не менять.</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Плановое время начала наладки; <c>null</c> — не менять.</summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }

    /// <summary>Явно очистить время начала наладки (в PUT <c>null</c> означает «не менять»).</summary>
    public bool ClearPlannedSetupStartTime { get; set; }

    /// <summary>Рабочий режим; <c>null</c> — не менять.</summary>
    public WorkMode? WorkMode { get; set; }

    /// <summary>Эталон полного цикла, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
    /// <summary>Явно очистить необязательный эталон цикла литья.</summary>
    public bool ClearPlannedInjectionCycleSeconds { get; set; }

    /// <summary>Норматив наладки, минуты; <c>null</c> — не менять.</summary>
    public int? PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фикс. часть, минуты; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferFixedMinutes { get; set; }

    /// <summary>Буфер выпуска: процент; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferPercent { get; set; }
}
