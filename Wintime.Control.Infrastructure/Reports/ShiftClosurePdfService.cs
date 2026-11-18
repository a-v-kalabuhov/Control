using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Reports;

public sealed class ShiftClosurePdfService : IShiftClosurePdfService
{
    public byte[] RenderBlank(ShiftClosureDto draft, DateTime generatedAtUtc) =>
        Render(ShiftClosurePdfModelFactory.CreateBlank(draft, generatedAtUtc));

    public byte[] RenderClosed(ShiftClosureDto closed) =>
        Render(ShiftClosurePdfModelFactory.CreateClosed(closed));

    private static byte[] Render(ShiftClosurePdfModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(6, Unit.Millimetre);
            page.DefaultTextStyle(style => style.FontSize(6));

            page.Header().Column(header =>
            {
                header.Item().Text($"Сменный рапорт на {model.ShiftDate:dd.MM.yyyy}, " +
                    $"{model.StartAtLocal:HH:mm}–{model.EndAtLocal:HH:mm}")
                    .SemiBold().FontSize(14);
                header.Item().Text(model.Mode).SemiBold().FontSize(9);
                if (model.GeneratedAtLocal is { } generatedAt)
                    header.Item().Text($"Сформировано: {generatedAt:dd.MM.yyyy HH:mm:ss}");
                header.Item().Text($"Старший смены: {model.ShiftLeadName}");
                if (!model.IsBlank)
                    header.Item().Text($"Закрыл: {model.ClosedByName}; " +
                        $"время закрытия: {model.ClosedAtLocal:dd.MM.yyyy HH:mm:ss}");
                if (model.EarlyCloseMessage != null)
                    header.Item().Text(model.EarlyCloseMessage).SemiBold().FontColor(Colors.Red.Medium);
            });

            page.Content().PaddingTop(4).Column(content =>
            {
                content.Item().Table(table => BuildTable(table, model.Entries));
                content.Item().PaddingTop(8).Row(signatures =>
                {
                    signatures.RelativeItem().Text(
                        $"Старший смены: {model.ShiftLeadName}    Подпись: " +
                        $"{model.ShiftLeadSignature}____________________");
                    if (!model.IsBlank)
                        signatures.RelativeItem().AlignRight().Text(
                            $"Закрыл: {model.ClosedByName}    Подпись: " +
                            $"{model.ClosedBySignature}____________________");
                });
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Страница ");
                text.CurrentPageNumber();
                text.Span(" из ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void BuildTable(TableDescriptor table, IReadOnlyList<ShiftClosurePdfEntryModel> entries)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.RelativeColumn(1.4f); // ТПА
            columns.RelativeColumn(1.8f); // Наименование
            columns.RelativeColumn(0.8f); // Остаток
            columns.RelativeColumn(1.8f); // Сырьё
            columns.RelativeColumn(1.5f); // Цвет/краситель
            columns.RelativeColumn(0.7f); // Вес норма
            columns.RelativeColumn(0.7f); // Вес изделия
            columns.RelativeColumn(0.7f); // Цикл норма
            columns.RelativeColumn(0.8f); // Цикл автоматический
            columns.RelativeColumn(0.7f); // Цикл ручной
            columns.RelativeColumn(0.8f); // Тарная единица
            columns.RelativeColumn(0.7f); // Количество мест
            columns.RelativeColumn(0.7f); // Количество гнёзд
            columns.RelativeColumn(0.7f); // План
            columns.RelativeColumn(0.8f); // Выпуск автоматический
            columns.RelativeColumn(0.8f); // Годные
            columns.RelativeColumn(0.6f); // Брак
            columns.RelativeColumn(0.8f); // Принято на склад
            columns.RelativeColumn(0.8f); // Номер
            columns.RelativeColumn(1.5f); // Литейщик
            columns.RelativeColumn(0.8f); // Автовыпуск литейщика
            columns.RelativeColumn(1.0f); // Подпись
            columns.RelativeColumn(0.8f); // Расход
        });

        table.Header(header =>
        {
            header.Cell().RowSpan(2).Element(HeaderCell).Text("ТПА");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Наименование");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Остаток на начало смены");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Сырьё");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Цвет/краситель");
            header.Cell().ColumnSpan(2).Element(HeaderCell).Text("Вес, г");
            header.Cell().ColumnSpan(3).Element(HeaderCell).Text("Цикл, с");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Тарная единица");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Количество мест");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Количество гнёзд");
            header.Cell().ColumnSpan(4).Element(HeaderCell).Text("Выпуск");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Принято на склад");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Номер");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Литейщик");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Автовыпуск литейщика");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Подпись");
            header.Cell().RowSpan(2).Element(HeaderCell).Text("Расход, г");

            header.Cell().Element(HeaderCell).Text("Норма");
            header.Cell().Element(HeaderCell).Text("Изделие");
            header.Cell().Element(HeaderCell).Text("Норма");
            header.Cell().Element(HeaderCell).Text("Авто");
            header.Cell().Element(HeaderCell).Text("Ручной");
            header.Cell().Element(HeaderCell).Text("План");
            header.Cell().Element(HeaderCell).Text("Авто");
            header.Cell().Element(HeaderCell).Text("Годные");
            header.Cell().Element(HeaderCell).Text("Брак");
        });

        foreach (var entry in entries)
        {
            var rowCount = Math.Max(1, entry.Operators.Count);
            var span = (uint)rowCount;
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.ImmName);
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.ProductName);
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.RemainingAtStart.ToString(CultureInfo.InvariantCulture));
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.PlasticIngredients);
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.ColorantIngredients);
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.NormPartWeightGrams));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.ActualPartWeightGrams));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.PlannedFullCycleSeconds));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.AutomaticAverageFullCycleSeconds));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.ManualFullCycleSeconds));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.DisplayPackagingUnit));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.PackagingPlaces));
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.Cavities.ToString(CultureInfo.InvariantCulture));
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.ShiftPlanQuantity.ToString(CultureInfo.InvariantCulture));
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.AutomaticOutputQuantity.ToString(CultureInfo.InvariantCulture));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.ManualGoodQuantity));
            table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.ManualDefectQuantity));
            table.Cell().RowSpan(span).Element(DataCell).Text(entry.WarehouseAccepted);

            for (var index = 0; index < rowCount; index++)
            {
                var operatorRow = index < entry.Operators.Count ? entry.Operators[index] : null;
                table.Cell().Element(DataCell).Text(operatorRow?.EmployeeId ?? string.Empty);
                table.Cell().Element(DataCell).Text(operatorRow?.FullName ?? string.Empty);
                table.Cell().Element(DataCell).Text(operatorRow == null
                    ? string.Empty : operatorRow.Quantity.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(DataCell).Text(operatorRow?.Signature ?? string.Empty);
                if (index == 0)
                    table.Cell().RowSpan(span).Element(DataCell).Text(Number(entry.PlannedMaterialWeightGrams));
            }
        }
    }

    private static IContainer HeaderCell(IContainer container) => container
        .Background(Colors.Grey.Lighten3)
        .Border(0.5f)
        .BorderColor(Colors.Grey.Medium)
        .Padding(2)
        .AlignMiddle()
        .DefaultTextStyle(style => style.SemiBold());

    private static IContainer DataCell(IContainer container) => container
        .Border(0.5f)
        .BorderColor(Colors.Grey.Lighten1)
        .Padding(2)
        .AlignMiddle();

    private static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

internal sealed class ShiftClosurePdfModel
{
    public bool IsBlank { get; init; }
    public string Mode { get; init; } = string.Empty;
    public DateOnly ShiftDate { get; init; }
    public DateTime StartAtLocal { get; init; }
    public DateTime EndAtLocal { get; init; }
    public DateTime FactEndAtLocal { get; init; }
    public DateTime? GeneratedAtLocal { get; init; }
    public string ShiftLeadName { get; init; } = string.Empty;
    public string ShiftLeadSignature { get; init; } = string.Empty;
    public string? ClosedByName { get; init; }
    public DateTime? ClosedAtLocal { get; init; }
    public string ClosedBySignature { get; init; } = string.Empty;
    public string? EarlyCloseMessage { get; init; }
    public IReadOnlyList<ShiftClosurePdfEntryModel> Entries { get; init; } =
        Array.Empty<ShiftClosurePdfEntryModel>();
}

internal sealed class ShiftClosurePdfEntryModel
{
    public string ImmName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int RemainingAtStart { get; init; }
    public string PlasticIngredients { get; init; } = string.Empty;
    public string ColorantIngredients { get; init; } = string.Empty;
    public decimal NormPartWeightGrams { get; init; }
    public decimal? ActualPartWeightGrams { get; init; }
    public int? PlannedFullCycleSeconds { get; init; }
    public double? AutomaticAverageFullCycleSeconds { get; init; }
    public int? ManualFullCycleSeconds { get; init; }
    public int? TaskPackagingUnit { get; init; }
    public int? ActualPackagingUnit { get; init; }
    public int? DisplayPackagingUnit { get; init; }
    public decimal? PackagingPlaces { get; init; }
    public int Cavities { get; init; }
    public int ShiftPlanQuantity { get; init; }
    public decimal PlannedMaterialWeightGrams { get; init; }
    public int AutomaticOutputQuantity { get; init; }
    public int? ManualGoodQuantity { get; init; }
    public int? ManualDefectQuantity { get; init; }
    public string WarehouseAccepted { get; init; } = string.Empty;
    public IReadOnlyList<ShiftClosurePdfShareModel> Shares { get; init; } =
        Array.Empty<ShiftClosurePdfShareModel>();
    public IReadOnlyList<ShiftClosurePdfOperatorModel> Operators { get; init; } =
        Array.Empty<ShiftClosurePdfOperatorModel>();
}

internal sealed class ShiftClosurePdfShareModel
{
    public DateTime IntervalStartLocal { get; init; }
    public DateTime IntervalEndLocal { get; init; }
    public string OperatorId { get; init; } = string.Empty;
    public string OperatorFullName { get; init; } = string.Empty;
    public string? OperatorEmployeeId { get; init; }
    public int SlotAtIntervalStart { get; init; }
    public int Quantity { get; init; }
}

internal sealed class ShiftClosurePdfOperatorModel
{
    public string OperatorId { get; init; } = string.Empty;
    public string EmployeeId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public string Signature { get; init; } = string.Empty;
}

internal static class ShiftClosurePdfModelFactory
{
    public static ShiftClosurePdfModel CreateBlank(ShiftClosureDto draft, DateTime generatedAtUtc)
    {
        if (draft.State == ShiftClosureState.Closed)
            throw new DomainException("shiftClosed");
        return Create(draft, isBlank: true, generatedAtUtc);
    }

    public static ShiftClosurePdfModel CreateClosed(ShiftClosureDto closed)
    {
        if (closed.State != ShiftClosureState.Closed)
            throw new DomainException("shiftNotClosed");
        return Create(closed, isBlank: false, generatedAtUtc: null);
    }

    private static ShiftClosurePdfModel Create(
        ShiftClosureDto dto, bool isBlank, DateTime? generatedAtUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(dto.TimeZoneId);
        DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(Utc(utc), timeZone);
        DateTime? LocalNullable(DateTime? utc) => utc.HasValue ? Local(utc.Value) : null;
        var closedAtLocal = LocalNullable(dto.ClosedAt);
        var endAtLocal = Local(dto.EndAtUtc);
        return new ShiftClosurePdfModel
        {
            IsBlank = isBlank,
            Mode = isBlank ? "Бланк — смена не закрыта" : "Закрытый рапорт",
            ShiftDate = dto.ShiftDate,
            StartAtLocal = Local(dto.StartAtUtc),
            EndAtLocal = endAtLocal,
            FactEndAtLocal = Local(dto.FactEndUtc),
            GeneratedAtLocal = isBlank ? LocalNullable(generatedAtUtc) : null,
            ShiftLeadName = dto.ShiftLeadName,
            ShiftLeadSignature = string.Empty,
            ClosedByName = isBlank ? null : dto.ClosedByName,
            ClosedAtLocal = isBlank ? null : closedAtLocal,
            ClosedBySignature = string.Empty,
            EarlyCloseMessage = !isBlank && dto.ClosedEarly && closedAtLocal.HasValue
                ? $"Смена закрыта досрочно: фактическое время закрытия {closedAtLocal.Value:dd.MM.yyyy HH:mm}, " +
                  $"плановое окончание {endAtLocal:dd.MM.yyyy HH:mm}"
                : null,
            Entries = dto.Entries.Select(entry => Entry(entry, isBlank, Local)).ToList()
        };
    }

    private static ShiftClosurePdfEntryModel Entry(
        ShiftClosureEntryDto entry, bool clearManual, Func<DateTime, DateTime> local)
    {
        var shares = entry.OutputShares.Select(share => new ShiftClosurePdfShareModel
        {
            IntervalStartLocal = local(share.IntervalStartUtc),
            IntervalEndLocal = local(share.IntervalEndUtc),
            OperatorId = share.OperatorId,
            OperatorFullName = share.OperatorFullName,
            OperatorEmployeeId = share.OperatorEmployeeId,
            SlotAtIntervalStart = share.SlotAtIntervalStart,
            Quantity = share.Quantity
        }).ToList();
        var operators = shares.GroupBy(share => share.OperatorId)
            .Select(group =>
            {
                var first = group.First();
                return new ShiftClosurePdfOperatorModel
                {
                    OperatorId = group.Key,
                    EmployeeId = first.OperatorEmployeeId ?? string.Empty,
                    FullName = first.OperatorFullName,
                    Quantity = group.Sum(share => share.Quantity),
                    Signature = string.Empty
                };
            })
            .OrderBy(operatorRow => operatorRow.FullName)
            .ThenBy(operatorRow => operatorRow.OperatorId, StringComparer.Ordinal)
            .ToList();
        return new ShiftClosurePdfEntryModel
        {
            ImmName = entry.ImmName,
            ProductName = entry.ProductName,
            RemainingAtStart = entry.RemainingAtStart,
            PlasticIngredients = entry.PlasticIngredients,
            ColorantIngredients = entry.ColorantIngredients,
            NormPartWeightGrams = entry.NormPartWeightGrams,
            ActualPartWeightGrams = clearManual ? null : entry.ActualPartWeightGrams,
            PlannedFullCycleSeconds = entry.PlannedFullCycleSeconds,
            AutomaticAverageFullCycleSeconds = entry.AutomaticAverageFullCycleSeconds,
            ManualFullCycleSeconds = clearManual ? null : entry.ManualFullCycleSeconds,
            TaskPackagingUnit = entry.TaskPackagingUnit,
            ActualPackagingUnit = clearManual ? null : entry.ActualPackagingUnit,
            DisplayPackagingUnit = clearManual
                ? entry.TaskPackagingUnit
                : entry.ActualPackagingUnit ?? entry.TaskPackagingUnit,
            PackagingPlaces = clearManual ? null : entry.PackagingPlaces,
            Cavities = entry.Cavities,
            ShiftPlanQuantity = entry.ShiftPlanQuantity,
            PlannedMaterialWeightGrams = entry.PlannedMaterialWeightGrams,
            AutomaticOutputQuantity = entry.AutomaticOutputQuantity,
            ManualGoodQuantity = clearManual ? null : entry.ManualGoodQuantity,
            ManualDefectQuantity = clearManual ? null : entry.ManualDefectQuantity,
            WarehouseAccepted = string.Empty,
            Shares = shares,
            Operators = operators
        };
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
