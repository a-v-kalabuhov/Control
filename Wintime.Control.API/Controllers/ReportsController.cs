using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Services.Reports;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IPdfReportService _pdfService;

    public ReportsController(IReportService reportService, IPdfReportService pdfService)
    {
        _reportService = reportService;
        _pdfService = pdfService;
    }

    /// <summary>
    /// Отчёт "Картина рабочего дня"
    /// </summary>
    [HttpGet("daily")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<DailyReportDto>> GetDailyReport(
        [FromQuery] DateTime date,
        [FromQuery] Guid? immId = null)
    {
        try
        {
            var report = await _reportService.GetDailyReportAsync(date, immId);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка генерации отчёта", message = ex.Message });
        }
    }

    /// <summary>
    /// Отчёт "Производительность оборудования"
    /// </summary>
    [HttpGet("equipment")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<EquipmentReportDto>> GetEquipmentReport(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] List<Guid>? immIds = null)
    {
        try
        {
            var report = await _reportService.GetEquipmentReportAsync(dateFrom, dateTo, immIds);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка генерации отчёта", message = ex.Message });
        }
    }

    /// <summary>
    /// Отчёт "Активы цеха"
    /// </summary>
    [HttpGet("assets")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<AssetsReportDto>> GetAssetsReport(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string reportType)
    {
        try
        {
            var report = await _reportService.GetAssetsReportAsync(dateFrom, dateTo, reportType);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка генерации отчёта", message = ex.Message });
        }
    }

    /// <summary>
    /// Экспорт отчёта в Excel
    /// </summary>
    [HttpPost("export/excel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> ExportReportExcel([FromBody] ExportReportRequestDto request)
    {
        try
        {
            object reportData = request.ReportType.ToLower() switch
            {
                "daily" => await _reportService.GetDailyReportAsync(request.DateFrom, request.ImmIds?.FirstOrDefault()),
                "equipment" => await _reportService.GetEquipmentReportAsync(request.DateFrom, request.DateTo, request.ImmIds),
                "assets" => await _reportService.GetAssetsReportAsync(request.DateFrom, request.DateTo, request.ReportType),
                _ => throw new ArgumentException("Неизвестный тип отчёта")
            };

            var excelBytes = await _reportService.ExportToExcelAsync(reportData, request.ReportType);

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                $"Report_{request.ReportType}_{request.DateFrom:yyyyMMdd}.xlsx");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка экспорта", message = ex.Message });
        }
    }

    /// <summary>
    /// Экспорт отчёта в PDF
    /// </summary>
    [HttpPost("export/pdf")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> ExportReportPdf([FromBody] ExportReportRequestDto request)
    {
        try
        {
            object report = request.ReportType.ToLower() switch
            {
                "daily" => await _reportService.GetDailyReportAsync(request.DateFrom, request.ImmIds?.FirstOrDefault()),
                "equipment" => await _reportService.GetEquipmentReportAsync(request.DateFrom, request.DateTo, request.ImmIds),
                "assets" => await _reportService.GetAssetsReportAsync(request.DateFrom, request.DateTo, request.ReportType),
                _ => throw new ArgumentException("Неизвестный тип отчёта")
            };

            var pdfBytes = request.ReportType.ToLower() switch
            {
                "daily" => _pdfService.GenerateDailyReportPdf((DailyReportDto)report),
                // TODO: Добавить генераторы для других типов отчётов
                _ => throw new NotImplementedException($"PDF для {request.ReportType} не реализован")
            };

            return File(pdfBytes, "application/pdf", 
                $"Report_{request.ReportType}_{request.DateFrom:yyyyMMdd}.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка генерации PDF", message = ex.Message });
        }
    }
}