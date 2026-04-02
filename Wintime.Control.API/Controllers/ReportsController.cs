using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    // TODO: Inject Report Service
    public ReportsController()
    {
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
        // TODO: Реализовать бизнес-логику отчёта
        var report = new DailyReportDto
        {
            Date = date,
            ImmData = new List<DailyReportImmItemDto>()
        };

        return Ok(report);
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
        // TODO: Реализовать бизнес-логику отчёта
        var report = new EquipmentReportDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ImmData = new List<EquipmentReportImmItemDto>()
        };

        return Ok(report);
    }

    /// <summary>
    /// Отчёт "Активы цеха" (ПФ и наладчики)
    /// </summary>
    [HttpGet("assets")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<AssetsReportDto>> GetAssetsReport(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] string reportType)
    {
        // TODO: Реализовать бизнес-логику отчёта
        var report = new AssetsReportDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            ReportType = reportType
        };

        return Ok(report);
    }

    /// <summary>
    /// Экспорт отчёта в Excel
    /// </summary>
    [HttpPost("export/excel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> ExportReportExcel([FromBody] ExportReportRequestDto request)
    {
        // TODO: Реализовать генерацию Excel через ClosedXML
        return NotImplemented;
    }

    /// <summary>
    /// Экспорт отчёта в PDF
    /// </summary>
    [HttpPost("export/pdf")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> ExportReportPdf([FromBody] ExportReportRequestDto request)
    {
        // TODO: Реализовать генерацию PDF
        return NotImplemented;
    }
}