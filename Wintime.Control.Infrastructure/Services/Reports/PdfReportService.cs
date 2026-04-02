using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Wintime.Control.Core.DTOs.Report;

namespace Wintime.Control.Core.Services.Reports;

public interface IPdfReportService
{
    byte[] GenerateDailyReportPdf(DailyReportDto report);
}

public class PdfReportService : IPdfReportService
{
    public byte[] GenerateDailyReportPdf(DailyReportDto report)
    {
        // Настройка лицензии (бесплатно для малого бизнеса)
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                // Шапка
                page.Header()
                    .Text($"Отчёт: Картина рабочего дня ({report.Date:dd.MM.yyyy})")
                    .SemiBold().FontSize(16).FontColor(Colors.Blue.Medium);

                // Таблица
                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Table(table =>
                    {
                        // Заголовки
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); // ТПА
                            columns.RelativeColumn(2); // ПФ
                            columns.ConstantColumn(1);  // План
                            columns.ConstantColumn(1);  // Факт
                            columns.ConstantColumn(1.5f); // Эффективность
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("ТПА");
                            header.Cell().Element(CellStyle).Text("Пресс-форма");
                            header.Cell().Element(CellStyle).Text("План");
                            header.Cell().Element(CellStyle).Text("Факт");
                            header.Cell().Element(CellStyle).Text("Эфф., %");

                            static IContainer CellStyle(IContainer container) => 
                                container.DefaultTextStyle(x => x.SemiBold()).PaddingBottom(5);
                        });

                        // Данные
                        foreach (var item in report.ImmData)
                        {
                            table.Cell().Element(CellStyle).Text(item.ImmName);
                            table.Cell().Element(CellStyle).Text(item.MoldName ?? "—");
                            table.Cell().Element(CellStyle).Text(item.PlanQuantity.ToString());
                            table.Cell().Element(CellStyle).Text(item.ActualQuantity.ToString());
                            table.Cell().Element(CellStyle).Text($"{item.Efficiency:F1}%");

                            static IContainer CellStyle(IContainer container) => 
                                container.PaddingBottom(3);
                        }
                    });

                // Футер
                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Страница ");
                        x.CurrentPageNumber();
                        x.Span(" из ");
                        x.TotalPages();
                    });
            });
        }).GeneratePdf();
    }
}