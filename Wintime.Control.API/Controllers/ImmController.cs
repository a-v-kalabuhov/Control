using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;
using Wintime.Control.Shared.Settings;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImmController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly IImmStatusCache _statusCache;
    private readonly IImmCache _immCache;
    private readonly DowntimeSettings _downtime;
    private readonly ILogger<ImmController> _logger;

    public ImmController(ControlDbContext context, IImmStatusCache statusCache, IImmCache immCache,
        IOptions<DowntimeSettings> downtime, ILogger<ImmController> logger)
    {
        _context = context;
        _statusCache = statusCache;
        _immCache = immCache;
        _downtime = downtime.Value;
        _logger = logger;
    }

    /// <summary>
    /// Список всех ТПА (IMM)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer},{Roles.Emulator}")]
    public async Task<ActionResult<IEnumerable<ImmDto>>> GetImmList([FromQuery] bool? isActive = null)
    {
        var query = _context.Imms
            .Include(i => i.Template)
            .AsQueryable();

        if (isActive.HasValue)
            query = query.Where(i => i.IsActive == isActive.Value);

        var imms = await query
            .Select(i => new ImmDto
            {
                Id = i.Id,
                Name = i.Name,
                InventoryNumber = i.InventoryNumber,
                ConnectorAlias = i.ConnectorAlias,
                TemplateId = i.TemplateId,
                Manufacturer = i.Template.Manufacturer,
                Model = i.Template.Model,
                IsActive = i.IsActive,
                CreatedAt = i.CreatedAt,
                CommissioningDate = i.CommissioningDate,
                CurrentTaskId = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefault(),
                CurrentMoldName = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => t.Mold.Name)
                    .FirstOrDefault(),
                PersonnelName = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => t.Personnel != null ? t.Personnel.FullName : null)
                    .FirstOrDefault(),
                PlanQuantity = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => (int?)t.PlanQuantity)
                    .FirstOrDefault(),
                ActualQuantity = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => (int?)t.ActualQuantity)
                    .FirstOrDefault(),
                TaskStartedAt = i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => t.StartedAt)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var currentTaskIds = imms
            .Where(i => i.CurrentTaskId.HasValue)
            .Select(i => i.CurrentTaskId!.Value)
            .ToList();

        if (currentTaskIds.Count > 0)
        {
            var cycleStats = await _context.ImmCycles
                .Where(c => c.TaskId.HasValue && currentTaskIds.Contains(c.TaskId.Value))
                .GroupBy(c => c.TaskId!.Value)
                .Select(g => new
                {
                    TaskId = g.Key,
                    Count = g.Count(),
                    AvgDuration = g.Average(c => (double)c.DurationSeconds)
                })
                .ToListAsync();

            foreach (var dto in imms.Where(d => d.CurrentTaskId.HasValue))
            {
                var stats = cycleStats.FirstOrDefault(s => s.TaskId == dto.CurrentTaskId!.Value);
                if (stats != null)
                {
                    dto.CycleCount = stats.Count;
                    dto.AvgCycleTime = (decimal)stats.AvgDuration;
                }
            }
        }

        var activeTaskStatuses = await query
            .Select(i => new
            {
                ImmId = i.Id,
                TaskStatus = (Core.Enums.TaskStatus?)i.ShiftTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup)
                    .Select(t => (Core.Enums.TaskStatus?)t.Status)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.ImmId, x => x.TaskStatus);

        var openDowntimeImmIds = (await _context.Events
            .Where(e => e.EventType == Core.Enums.EventType.Downtime && e.EndTime == null)
            .Select(e => e.ImmId)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        foreach (var dto in imms)
        {
            var statusEntry = _statusCache.GetEntry(dto.Id);
            var cacheEntry = _immCache.GetEntry(dto.Id);
            dto.Status = statusEntry?.Status ?? ImmStatus.Offline;
            dto.LastUpdate = MaxDateTime(statusEntry?.SinceUtc, cacheEntry?.LastMessageAt);

            var rawForEff = statusEntry?.Status ?? ImmStatus.Offline;
            var taskForEff = Core.Enums.ActiveTaskStatusMap.From(
                activeTaskStatuses.TryGetValue(dto.Id, out var ts) ? ts : null);
            var hasOpenDt = openDowntimeImmIds.Contains(dto.Id);
            var thresholdPassed = statusEntry != null &&
                (DateTime.UtcNow - statusEntry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
            dto.EffectiveStatus = ImmEffectiveStatus.Resolve(rawForEff, taskForEff, hasOpenDt, thresholdPassed);
        }

        return Ok(imms);
    }

    /// <summary>
    /// Получить данные ТПА по ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer},{Roles.Emulator}")]
    public async Task<ActionResult<ImmDto>> GetImmById(Guid id)
    {
        var imm = await _context.Imms
            .Include(i => i.Template)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (imm == null)
            return NotFound();

        var dto = new ImmDto
        {
            Id = imm.Id,
            Name = imm.Name,
            InventoryNumber = imm.InventoryNumber,
            ConnectorAlias = imm.ConnectorAlias,
            TemplateId = imm.TemplateId,
            Manufacturer = imm.Template.Manufacturer,
            Model = imm.Template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt,
            CommissioningDate = imm.CommissioningDate,
            Status = _statusCache.GetStatus(imm.Id)
        };

        return Ok(dto);
    }

    /// <summary>
    /// Создать новый ТПА (IMM)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ImmDto>> CreateImm([FromBody] CreateImmRequestDto request)
    {
        var template = await _context.Templates.FindAsync(request.TemplateId);
        if (template == null)
            return BadRequest("Шаблон оборудования не найден");

        var imm = new Imm
        {
            Name = request.Name,
            InventoryNumber = request.InventoryNumber,
            ConnectorAlias = request.ConnectorAlias,
            CommissioningDate = request.CommissioningDate.HasValue
                ? DateTime.SpecifyKind(request.CommissioningDate.Value.Date, DateTimeKind.Utc)
                : null,
            TemplateId = request.TemplateId,
            IsActive = true
        };

        _context.Imms.Add(imm);
        await _context.SaveChangesAsync();

        var dto = new ImmDto
        {
            Id = imm.Id,
            Name = imm.Name,
            InventoryNumber = imm.InventoryNumber,
            ConnectorAlias = imm.ConnectorAlias,
            TemplateId = imm.TemplateId,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt,
            CommissioningDate = imm.CommissioningDate,
            Status = _statusCache.GetStatus(imm.Id)
        };

        return CreatedAtAction(nameof(GetImmById), new { id = imm.Id }, dto);
    }

    /// <summary>
    /// Обновить данные ТПА
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateImm(Guid id, [FromBody] UpdateImmRequestDto request)
    {
        var imm = await _context.Imms.FindAsync(id);
        if (imm == null)
            return NotFound();

        if (request.Name != null)
            imm.Name = request.Name;
        if (request.InventoryNumber != null)
            imm.InventoryNumber = request.InventoryNumber;
        if (request.ConnectorAlias != null)
            imm.ConnectorAlias = request.ConnectorAlias;
        if (request.CommissioningDate.HasValue)
            imm.CommissioningDate = DateTime.SpecifyKind(request.CommissioningDate.Value.Date, DateTimeKind.Utc);
        if (request.IsActive.HasValue)
            imm.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Текущий статус ТПА (для дашборда)
    /// </summary>
    [HttpGet("{id:guid}/status")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer},{Roles.Emulator}")]
    public async Task<ActionResult<ImmStatusDto>> GetImmStatus(Guid id)
    {
        var imm = await _context.Imms.FindAsync(id);
        if (imm == null)
            return NotFound();

        var currentTask = await _context.ShiftTasks
            .FirstOrDefaultAsync(t => t.ImmId == id &&
                (t.Status == Core.Enums.TaskStatus.InProgress || t.Status == Core.Enums.TaskStatus.Setup));

        var entry = _statusCache.GetEntry(id);

        var cacheEntry = _immCache.GetEntry(id);

        var hasOpenDt = await _context.Events.AnyAsync(e =>
            e.ImmId == id && e.EventType == Core.Enums.EventType.Downtime && e.EndTime == null);
        var taskForEff = Core.Enums.ActiveTaskStatusMap.From(currentTask?.Status);
        var rawForEff = entry?.Status ?? ImmStatus.Offline;
        var thresholdPassed = entry != null &&
            (DateTime.UtcNow - entry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
        var effective = ImmEffectiveStatus.Resolve(rawForEff, taskForEff, hasOpenDt, thresholdPassed);

        return Ok(new ImmStatusDto
        {
            ImmId = imm.Id,
            Status = entry?.Status ?? ImmStatus.Offline,
            EffectiveStatus = effective,
            CurrentTaskId = currentTask?.Id,
            CurrentMoldId = currentTask?.MoldId,
            CurrentCycleTime = 0, // TODO: Вычислить из телеметрии
            LastUpdate = MaxDateTime(entry?.SinceUtc, cacheEntry?.LastMessageAt) ?? DateTime.MinValue
        });
    }

    /// <summary>
    /// История телеметрии ТПА
    /// </summary>
    [HttpGet("{id:guid}/telemetry")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<TelemetryDto>>> GetImmTelemetry(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] List<string>? parameters = null)
    {
        var query = _context.Telemetry
            .Where(t => t.ImmId == id && t.Timestamp >= from && t.Timestamp <= to)
            .AsQueryable();

        if (parameters != null && parameters.Any())
        {
            query = query.Where(t => parameters.Contains(t.ParameterName));
        }

        var telemetry = await query
            .OrderBy(t => t.Timestamp)
            .Select(t => new TelemetryDto
            {
                Timestamp = t.Timestamp,
                ParameterName = t.ParameterName,
                ValueNumeric = t.ValueNumeric,
                ValueText = t.ValueText
            })
            .ToListAsync();

        return Ok(telemetry);
    }

    /// <summary>
    /// История статусов ТПА за период (для таймлайна смены)
    /// </summary>
    [HttpGet("{id:guid}/status-history")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<IEnumerable<ImmStatusSegmentDto>>> GetImmStatusHistory(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var immExists = await _context.Imms.AnyAsync(i => i.Id == id);
        if (!immExists)
            return NotFound();

        var segments = await _context.ImmStatusHistory
            .Where(h => h.ImmId == id && h.ChangedAt < to && (h.EndedAt == null || h.EndedAt > from))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new ImmStatusSegmentDto
            {
                Status = h.Status,
                ChangedAt = h.ChangedAt,
                EndedAt = h.EndedAt
            })
            .ToListAsync();

        return Ok(segments);
    }

    /// <summary>
    /// История эффективного состояния ТПА за период (реконструкция наложением рядов).
    /// </summary>
    [HttpGet("{id:guid}/effective-status-history")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<IEnumerable<EffectiveStatusSegmentDto>>> GetImmEffectiveStatusHistory(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var immExists = await _context.Imms.AnyAsync(i => i.Id == id);
        if (!immExists)
            return NotFound();

        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);
        var nowUtc = DateTime.UtcNow;
        var effectiveTo = toUtc < nowUtc ? toUtc : nowUtc;
        DateTime ClampEnd(DateTime? end) => (end ?? effectiveTo) > effectiveTo ? effectiveTo : (end ?? effectiveTo);

        var rawRows = await _context.ImmStatusHistory
            .Where(h => h.ImmId == id && h.ChangedAt < toUtc && (h.EndedAt == null || h.EndedAt > fromUtc))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.Status, h.ChangedAt, h.EndedAt })
            .ToListAsync();

        var taskRows = await _context.ShiftTasks
            .Where(t => t.ImmId == id && t.SetupStartedAt != null && t.SetupStartedAt < toUtc)
            .Select(t => new { t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt })
            .ToListAsync();

        var downtimeRows = await _context.Events
            .Where(e => e.ImmId == id && e.EventType == Core.Enums.EventType.Downtime
                        && e.StartTime < toUtc && (e.EndTime == null || e.EndTime > fromUtc))
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync();

        var raw = rawRows
            .Select(r => new RawSegment(r.Status, r.ChangedAt, ClampEnd(r.EndedAt)))
            .ToList();

        var tasks = new List<TaskInterval>();
        foreach (var t in taskRows)
        {
            var setupStart = t.SetupStartedAt!.Value;
            var setupEnd   = t.StartedAt ?? t.CompletedAt ?? t.ClosedAt ?? toUtc;
            tasks.Add(new TaskInterval(Core.Enums.ActiveTaskStatus.Setup, setupStart, ClampEnd(setupEnd)));
            if (t.StartedAt != null)
            {
                var workEnd = t.CompletedAt ?? t.ClosedAt ?? toUtc;
                tasks.Add(new TaskInterval(Core.Enums.ActiveTaskStatus.InProgress, t.StartedAt.Value, ClampEnd(workEnd)));
            }
        }

        var downtimes = downtimeRows
            .Select(d => new Interval(d.StartTime, ClampEnd(d.EndTime)))
            .ToList();

        var segments = EffectiveStatusTimeline.Build(raw, tasks, downtimes, fromUtc, effectiveTo);

        var dto = segments.Select(s => new EffectiveStatusSegmentDto
        {
            EffectiveStatus = s.EffectiveStatus,
            ChangedAt = s.Start,
            EndedAt = s.End,
        });

        return Ok(dto);
    }

    /// <summary>
    /// Получить статистику по циклам за период
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public Task<ActionResult<ImmStatisticsDto>> GetImmStatistics(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? taskId = null)
    {
        // TODO: Реализовать агрегацию данных из событий и телеметрии
        var statistics = new ImmStatisticsDto
        {
            ImmId = id,
            PeriodFrom = from,
            PeriodTo = to,
            TotalCycles = 0,
            CyclesByTask = new List<TaskCycleStatsDto>(),
            CyclesInSetup = 0,
            CyclesInAlarm = 0,
            AvgCycleTime = 0
        };

        return Task.FromResult<ActionResult<ImmStatisticsDto>>(Ok(statistics));
    }

    /// <summary>
    /// Получить QR-код для ТПА
    /// </summary>
    [HttpGet("{id:guid}/qr")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<QrCodeDto>> GetImmQr(Guid id)
    {
        var imm = await _context.Imms.FindAsync(id);
        if (imm == null)
            return NotFound();

        var qrData = JsonSerializer.Serialize(new
        {
            entity = "machine",
            id = imm.Id
        });

        var dto = new QrCodeDto
        {
            EntityType = "machine",
            EntityId = imm.Id.ToString(),
            QrData = qrData
        };

        return Ok(dto);
    }

    private static DateTime? MaxDateTime(DateTime? a, DateTime? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return a > b ? a : b;
    }
}