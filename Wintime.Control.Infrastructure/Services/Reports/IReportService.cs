using Wintime.Control.Core.DTOs.Report;

namespace Wintime.Control.Core.Services.Reports;

public interface IReportService
{
    // Отчёт "Картина рабочего дня"
    Task<DailyReportDto> GetDailyReportAsync(DateTime date, Guid? immId = null, CancellationToken ct = default);
    
    // Отчёт "Производительность оборудования"
    Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null, CancellationToken ct = default);
    
    // Отчёт "Активы цеха"
    Task<AssetsReportDto> GetAssetsReportAsync(DateTime dateFrom, DateTime dateTo, string reportType, CancellationToken ct = default);
    
    // Экспорт в Excel
    Task<byte[]> ExportToExcelAsync<T>(T data, string reportType, CancellationToken ct = default) where T : class;
}