using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MoldsController : ControllerBase
{
    private readonly ControlDbContext _context;

    public MoldsController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Список всех пресс-форм
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<MoldDto>>> GetMoldList(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        var query = _context.Molds.AsQueryable();

        if (isActive.HasValue)
        {
            query = query.Where(m => m.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(m => m.Name.Contains(search) || m.FormId.Contains(search));
        }

        var molds = await query.ToListAsync();

        var dtos = molds.Select(m => new MoldDto
        {
            Id = m.Id,
            FormId = m.FormId,
            Name = m.Name,
            Cavities = m.Cavities,
            PartWeightGrams = m.PartWeightGrams,
            RunnerWeightGrams = m.RunnerWeightGrams,
            MaxResourceCycles = m.MaxResourceCycles,
            To1Cycles = m.To1Cycles,
            To2Cycles = m.To2Cycles,
            StorageLocationIndex = m.StorageLocationIndex,
            DrawingPath = m.DrawingPath,
            PhotoPath = m.PhotoPath,
            TotalCycles = 0, // TODO: Вычислить из MoldUsage
            RemainingResource = 0, // TODO: Вычислить
            IsActive = m.IsActive
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Получить данные пресс-формы по ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<MoldDto>> GetMoldById(Guid id)
    {
        var mold = await _context.Molds.FindAsync(id);
        if (mold == null)
            return NotFound();

        // TODO: Вычислить TotalCycles и RemainingResource из MoldUsage
        var dto = new MoldDto
        {
            Id = mold.Id,
            FormId = mold.FormId,
            Name = mold.Name,
            Cavities = mold.Cavities,
            PartWeightGrams = mold.PartWeightGrams,
            RunnerWeightGrams = mold.RunnerWeightGrams,
            MaxResourceCycles = mold.MaxResourceCycles,
            To1Cycles = mold.To1Cycles,
            To2Cycles = mold.To2Cycles,
            StorageLocationIndex = mold.StorageLocationIndex,
            DrawingPath = mold.DrawingPath,
            PhotoPath = mold.PhotoPath,
            TotalCycles = 0,
            RemainingResource = mold.MaxResourceCycles,
            IsActive = mold.IsActive
        };

        return Ok(dto);
    }

    /// <summary>
    /// Создать новую пресс-форму
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<MoldDto>> CreateMold([FromBody] CreateMoldRequestDto request)
    {
        var mold = new Mold
        {
            FormId = $"PF-{Guid.NewGuid()}", // Генерируем уникальный FormID
            Name = request.Name,
            Cavities = request.Cavities,
            PartWeightGrams = request.PartWeightGrams,
            RunnerWeightGrams = request.RunnerWeightGrams,
            MaxResourceCycles = request.MaxResourceCycles,
            To1Cycles = request.To1Cycles,
            To2Cycles = request.To2Cycles,
            StorageLocationIndex = request.StorageLocationIndex,
            IsActive = true
        };

        _context.Molds.Add(mold);
        await _context.SaveChangesAsync();

        var dto = new MoldDto
        {
            Id = mold.Id,
            FormId = mold.FormId,
            Name = mold.Name,
            Cavities = mold.Cavities,
            PartWeightGrams = mold.PartWeightGrams,
            RunnerWeightGrams = mold.RunnerWeightGrams,
            MaxResourceCycles = mold.MaxResourceCycles,
            To1Cycles = mold.To1Cycles,
            To2Cycles = mold.To2Cycles,
            StorageLocationIndex = mold.StorageLocationIndex,
            DrawingPath = mold.DrawingPath,
            PhotoPath = mold.PhotoPath,
            TotalCycles = 0,
            RemainingResource = mold.MaxResourceCycles,
            IsActive = mold.IsActive
        };

        return CreatedAtAction(nameof(GetMoldById), new { id = mold.Id }, dto);
    }

    /// <summary>
    /// Обновить данные пресс-формы
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateMold(Guid id, [FromBody] UpdateMoldRequestDto request)
    {
        var mold = await _context.Molds.FindAsync(id);
        if (mold == null)
            return NotFound();

        if (request.Name != null)
            mold.Name = request.Name;
        if (request.Cavities.HasValue)
            mold.Cavities = request.Cavities.Value;
        if (request.StorageLocationIndex != null)
            mold.StorageLocationIndex = request.StorageLocationIndex;
        if (request.IsActive.HasValue)
            mold.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// История использования пресс-формы
    /// </summary>
    [HttpGet("{id:guid}/usage")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<MoldUsageDto>>> GetMoldUsage(
        Guid id,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var query = _context.MoldUsages
            .Include(mu => mu.Imm)
            .Include(mu => mu.Task)
            .Where(mu => mu.MoldId == id)
            .AsQueryable();

        if (from.HasValue)
            query = query.Where(mu => mu.StartTime >= from.Value);
        if (to.HasValue)
            query = query.Where(mu => mu.StartTime <= to.Value);

        var usages = await query.ToListAsync();

        var dtos = usages.Select(mu => new MoldUsageDto
        {
            MoldId = mu.MoldId,
            ImmId = mu.ImmId,
            ImmName = mu.Imm.Name,
            TaskId = mu.TaskId,
            StartTime = mu.StartTime,
            EndTime = mu.EndTime,
            CyclesStart = mu.CyclesStart,
            CyclesEnd = mu.CyclesEnd
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Получить QR-код для пресс-формы
    /// </summary>
    [HttpGet("{id:guid}/qr")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<QrCodeDto>> GetMoldQr(Guid id)
    {
        var mold = await _context.Molds.FindAsync(id);
        if (mold == null)
            return NotFound();

        var qrData = $"{{\"entity\":\"mold\",\"id\":\"{mold.FormId}\"}}";

        var dto = new QrCodeDto
        {
            EntityType = "mold",
            EntityId = mold.Id.ToString(),
            QrData = qrData
            // QrImageBase64 можно сгенерировать через библиотеку QRCode
        };

        return Ok(dto);
    }
}