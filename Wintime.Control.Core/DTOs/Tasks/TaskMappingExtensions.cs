using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.Tasks;

public static class TaskMappingExtensions
{
    /// <summary>
    /// Маппинг сущности задания в DTO. Имена ТПА/ПФ/изделия/наладчика заполняются,
    /// только если соответствующие навигации загружены (Include).
    /// План задания считается по его рабочей гнёздовости (PZP-36).
    /// </summary>
    public static TaskDto ToDto(this ShiftTask t, FactorySchedule schedule)
    {
        var plannedStartAt = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime);
        // Гнёзда — рабочая копия задания (прогноз), история остаётся в ImmCycle.Cavities.
        var plan = TaskPlanCalculator.Calculate(plannedStartAt, t.PlannedSetupMinutes, t.PlanQuantity,
            t.Cavities, t.PlannedFullCycleSeconds, t.ReleaseBufferFixedMinutes, t.ReleaseBufferPercent);

        return new TaskDto
        {
            Id = t.Id,
            ImmId = t.ImmId,
            ImmName = t.Imm?.Name,
            MoldId = t.MoldId,
            MoldName = t.Mold?.Name,
            ProductId = t.ProductId,
            ProductFullArticle = t.Product?.FullArticle,
            ProductName = t.Product?.Name,
            ProductTypeName = t.Product?.ProductType?.Name,
            ProductTypeCompany = t.Product?.ProductType?.Company,
            PersonnelId = t.PersonnelId,
            PersonnelName = t.Personnel?.FullName,
            PlanQuantity = t.PlanQuantity,
            Cavities = t.Cavities,
            PartWeightGrams = t.PartWeightGrams,
            PackagingUnit = t.PackagingUnit,
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
            OrderCustomer = t.Order?.Customer,
            OrderDate = t.Order?.OrderDate,
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
            // BL-36: копия рецепта. Навигации материалов загружаются Include в контроллерах;
            // без них маппинг не падает, но строки будут без артикула.
            RecipeIngredients = t.RecipeIngredients
                .OrderBy(i => i.SortOrder)
                .Select(i => new TaskRecipeIngredientDto
                {
                    Id = i.Id,
                    MaterialId = i.MaterialId,
                    MaterialType = i.Material != null ? i.Material.Type : default,
                    MaterialArticle = i.Material?.Article ?? string.Empty,
                    MaterialName = i.Material?.Name ?? string.Empty,
                    OriginalMaterialId = i.OriginalMaterialId,
                    OriginalMaterialArticle = i.OriginalMaterial?.Article ?? string.Empty,
                    OriginalMaterialName = i.OriginalMaterial?.Name ?? string.Empty,
                    Percent = i.Percent
                })
                .ToList(),
            RecipeEdited = t.RecipeEdited,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        };
    }
}
