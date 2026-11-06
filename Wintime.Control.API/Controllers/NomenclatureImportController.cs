using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Infrastructure.Nomenclature;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

/// <summary>Двухэтапный импорт номенклатуры из XLSX (PZP-20).</summary>
[ApiController]
[Route("api/nomenclature-import")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public class NomenclatureImportController : ControllerBase
{
    private const long MaxFileSize = 10 * 1024 * 1024;
    private readonly NomenclatureImportService _service;

    public NomenclatureImportController(NomenclatureImportService service) => _service = service;

    /// <summary>
    /// Без ошибок строк импортирует сразу. При ошибках и skipInvalidRows=false только возвращает отчёт;
    /// skipInvalidRows=true импортирует корректные строки, пропуская ошибочные.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<ActionResult<NomenclatureImportResult>> Upload(
        [FromForm] IFormFile file, [FromForm] bool skipInvalidRows = false,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            return BadRequest("Выберите непустой XLSX-файл.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Поддерживается только формат XLSX.");

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _service.ImportAsync(stream, skipInvalidRows, cancellationToken));
        }
        catch (InvalidDataException e)
        {
            return BadRequest(e.Message);
        }
    }
}
