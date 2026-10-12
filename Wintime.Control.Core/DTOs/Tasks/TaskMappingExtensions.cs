using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.Tasks;

public static class TaskMappingExtensions
{
    /// <summary>
    /// Маппинг сущности задания в DTO. Имена ТПА/ПФ/наладчика заполняются,
    /// только если соответствующие навигации загружены (Include),
    /// а план задания (PZP-14) — только если загружена <c>Mold</c>.
    /// </summary>
    public static TaskDto ToDto(this ShiftTask t, FactorySchedule schedule)
    {
        var plannedStartAt = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime);
        // Гнёзда — текущие Mold.Cavities (прогноз); Mold должен быть загружен (Include).
        var plan = TaskPlanCalculator.Calculate(plannedStartAt, t.PlannedSetupMinutes, t.PlanQuantity,
            t.Mold?.Cavities ?? 0, t.PlannedFullCycleSeconds, t.ReleaseBufferFixedMinutes, t.ReleaseBufferPercent);

        return new TaskDto
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
            PlannedStartAt = plannedStartAt,
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
            PlannedSetupMinutes = t.PlannedSetupMinutes,
            ReleaseBufferFixedMinutes = t.ReleaseBufferFixedMinutes,
            ReleaseBufferPercent = t.ReleaseBufferPercent,
            PlannedReleaseSeconds = plan.ReleaseSeconds,
            PlannedReleaseBufferSeconds = plan.BufferSeconds,
            PlannedEndAt = plan.PlannedEndAt,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }
}
