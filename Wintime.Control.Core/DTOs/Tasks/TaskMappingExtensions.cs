using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.Tasks;

public static class TaskMappingExtensions
{
    /// <summary>
    /// Маппинг сущности задания в DTO. Имена ТПА/ПФ/наладчика заполняются,
    /// только если соответствующие навигации загружены (Include).
    /// </summary>
    public static TaskDto ToDto(this ShiftTask t, FactorySchedule schedule) => new()
    {
        Id = t.Id,
        ImmId = t.ImmId,
        ImmName = t.Imm?.Name,
        MoldId = t.MoldId,
        MoldName = t.Mold?.Name,
        PersonnelId = t.PersonnelId,
        PersonnelName = t.Personnel?.FullName,
        PlanQuantity = t.PlanQuantity,
        ActualQuantity = t.ActualQuantity,
        ActualMaterialWeightGrams = t.ActualMaterialWeightGrams,
        ProgressPercent = t.PlanQuantity > 0 ? (decimal)t.ActualQuantity / t.PlanQuantity * 100 : 0,
        Status = t.Status,
        PlannedDate = t.PlannedDate,
        PlannedSetupStartTime = t.PlannedSetupStartTime,
        PlannedStartAt = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime),
        IssuedAt = t.IssuedAt,
        SetupStartedAt = t.SetupStartedAt,
        MoldVerifiedAt = t.MoldVerifiedAt,
        StartedAt = t.StartedAt,
        CompletedAt = t.CompletedAt,
        ClosedAt = t.ClosedAt,
        CloseReason = t.CloseReason,
        Note = t.Note,
        OrderId = t.OrderId,
        OrderNumber = t.Order?.Number,
        DefectQuantity = t.DefectQuantity,
        WorkMode = t.WorkMode,
        PlannedFullCycleSeconds = t.PlannedFullCycleSeconds,
        PlannedInjectionCycleSeconds = t.PlannedInjectionCycleSeconds,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
