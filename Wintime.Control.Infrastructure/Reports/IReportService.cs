using Wintime.Control.Core.DTOs.Report;

namespace Wintime.Control.Core.Services.Reports;

public interface IReportService
{
    /// <summary>
    /// Отчёт "Картина рабочего дня"
    /// </summary>
    /// <param name="date"></param>
    /// <param name="immId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<DailyReportDto> GetDailyReportAsync(DateTime date, Guid? immId = null, CancellationToken ct = default);
    
    /// <summary>
    /// Отчёт "Производительность оборудования"
    /// </summary>
    /// <param name="dateFrom"></param>
    /// <param name="dateTo"></param>
    /// <param name="immIds"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null, CancellationToken ct = default);
    
    /// <summary>
    /// Отчёт "Активы цеха"
    /// </summary>
    /// <param name="dateFrom"></param>
    /// <param name="dateTo"></param>
    /// <param name="reportType"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<AssetsReportDto> GetAssetsReportAsync(DateTime dateFrom, DateTime dateTo, string reportType, CancellationToken ct = default);
    
    /// <summary>
    /// Экспорт в Excel
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="data"></param>
    /// <param name="reportType"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<byte[]> ExportToExcelAsync<T>(T data, string reportType, CancellationToken ct = default) where T : class;
}