using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class CreateTaskRequestDto
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }

    /// <summary>Изделие задания (PZP-19). Обязательно (null → 400); принадлежит типу ПФ.</summary>
    public Guid? ProductId { get; set; }
    public Guid? OrderId { get; set; }
    public string? PersonnelId { get; set; }
    public int PlanQuantity { get; set; }
    public string? Note { get; set; }
    /// <summary>Плановая дата. Обязательна (null → 400).</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Плановое время начала наладки по часам завода. Необязательно.</summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }

    /// <summary>Рабочий режим; не передан — «автомат».</summary>
    public WorkMode WorkMode { get; set; } = WorkMode.Auto;

    /// <summary>Эталон полного цикла, секунды. Обязателен.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды. Необязателен.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }

    /// <summary>Норматив наладки, минуты (PZP-14). Не передан — из умолчаний.</summary>
    public int? PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фикс. часть, минуты. Не передан — из умолчаний.</summary>
    public int? ReleaseBufferFixedMinutes { get; set; }

    /// <summary>Буфер выпуска: % от плановой длительности выпуска. Не передан — из умолчаний.</summary>
    public int? ReleaseBufferPercent { get; set; }
}
