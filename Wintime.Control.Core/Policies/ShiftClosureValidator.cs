using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

public static class ShiftClosureValidator
{
    public static IReadOnlyList<ShiftClosureFieldError> Validate(
        ShiftClosureValidationEntry entry,
        bool requireCompleteness)
    {
        var errors = new List<ShiftClosureFieldError>();
        void Add(string field, string code) =>
            errors.Add(new ShiftClosureFieldError(entry.TaskId, field, code));

        if (entry.ActualPartWeightGrams is { } weight && weight <= 0)
            Add("actualPartWeightGrams", "mustBePositive");
        if (entry.ManualFullCycleSeconds is { } cycle && cycle <= 0)
            Add("manualFullCycleSeconds", "mustBePositive");
        if (entry.ActualPackagingUnit is { } packaging && packaging <= 0)
            Add("actualPackagingUnit", "mustBePositive");
        if (entry.PackagingPlaces is { } places)
        {
            if (places < 0)
                Add("packagingPlaces", "mustBeNonNegative");
            if (places != decimal.Round(places, 2))
                Add("packagingPlaces", "maxTwoDecimalPlaces");
        }
        if (entry.ManualGoodQuantity is { } good && good < 0)
            Add("manualGoodQuantity", "mustBeNonNegative");
        if (entry.ManualDefectQuantity is { } defect && defect < 0)
            Add("manualDefectQuantity", "mustBeNonNegative");

        if (!requireCompleteness)
            return errors;

        if (entry.HasOperators && entry.IsManualFallbackRequired)
        {
            if (entry.ManualFullCycleSeconds is null)
                Add("manualFullCycleSeconds", "required");
            if (entry.ManualGoodQuantity is null)
                Add("manualGoodQuantity", "required");
            if (entry.ManualDefectQuantity is null)
                Add("manualDefectQuantity", "required");
        }

        var manualOutput = (long)(entry.ManualGoodQuantity ?? 0) + (entry.ManualDefectQuantity ?? 0);
        var hasOutput = entry.AutomaticOutputQuantity > 0 || manualOutput > 0;
        if (hasOutput)
        {
            if (entry.ActualPartWeightGrams is null)
                Add("actualPartWeightGrams", "required");

            var effectivePackaging = entry.ActualPackagingUnit ?? entry.TaskPackagingUnit;
            if (effectivePackaging is not > 0 && entry.ActualPackagingUnit is null)
                Add("actualPackagingUnit", "required");
        }

        return errors;
    }

    public static void ValidateCloseTiming(
        DateTime nowUtc,
        DateTime startUtc,
        DateTime endUtc,
        bool confirmEarlyClose)
    {
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);

        if (nowUtc < startUtc)
        {
            throw new ShiftClosureValidationException(
                "closeBeforeShiftStart",
                "Нельзя закрыть смену до её начала",
                Array.Empty<ShiftClosureFieldError>());
        }

        if (nowUtc < endUtc && !confirmEarlyClose)
        {
            throw new ShiftClosureValidationException(
                "earlyCloseConfirmationRequired",
                "Для закрытия смены до её окончания требуется подтверждение",
                Array.Empty<ShiftClosureFieldError>());
        }
    }
}
