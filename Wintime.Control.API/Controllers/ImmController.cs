using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImmController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly IImmStatusCache _statusCache;

    public ImmController(ControlDbContext context, IImmStatusCache statusCache)
    {
        _context = context;
        _statusCache = statusCache;
    }

    /// <summary>
    /// Список всех ТПА (IMM)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer},{Roles.Emulator}")]
    public async Task<ActionResult<IEnumerable<ImmDto>>> GetImmList([FromQuery] bool? isActive = null)
    {
        /// Фронтэнд основного API требует получать статус IMM для корректного отображения. 
        /// Смотри здесь:
        /// @Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue  
        /// @Wintime-Control-Frontend/src/api/imm.js  
        /// @Wintime-Control-Frontend/src/components/dashboard/ImmStatusBadge.vue  
        /// @Wintime-Control-Frontend/src/constants/immStatus.js 
        /// Но imm.js обращается к  контроллеру Imm в бекэнде. Запрос Get этого контроллера возвращает список объектов ImmDto, у которых нет поля "статус".
        /// Как решить эту проблему? 
        /// Можно ввести для IMM отдельную сущность "статус" и использовать её вместе с ImmDto. 
        /// Статус - это по сути комбинация из режима работы и состояния доступности IMM.
        /// Режим работы определяется датчиком mode.
        /// А состояние доступности определяется в кеше IMM - у ImmCacheEntry есть функция IsOnline.
        /// 
        /// Тут есть проблема с датчиком mode. По сути он описывает некое глобальное состояние IMM.
        /// Это уникалный вид датчика. Он может быть только один в списке датчиков IMM.
        /// Сейчас он опписывается как строковой, но на самом деле надо ввести для него специальный тип "mode", по аналогии с типом "cycleCounter".
        /// Данные при этом останутся строковыми, но можно будет выделить этот датчик из списка именно по его типу.
        /// С другой стороны можно вынести датчик mode в отдельное поле в payload сообщения, по аналогии с полем timestamp.
        /// Т.е. сделать его не датчиком, а отдельным полем в json.
        /// Не знаю как лучше поступить.
        /// Если сравнивать с cycleCounter, то у некоторых видов оборудования может не быть циклов.
        /// Но режим работы у оборудования всегда есть. Сами строковые константы, описывающие режимы работы конкретного вида оборудоания могут быть разные, но сам редим есть у любого оборудования.
        /// Всегда есть как минимум два режима работы:
        /// 1. простой (idle - оборудование включено, но находится не под нагрузкой, потребление электроэнергии низкое, обрудование простаивает без пользы)
        /// 2. работа по программе (auto - оборудование включено и потребляет много энергии, т.к. делает полезную работу)
        /// Подскажи как лучше быть с датчиком mode? Сделать в конфиге уникальный тип датчика "mode" или вынести в отдельное поле в payload сообщения?
        /// 
        /// Состояние доступности оборудования имеет приоритет над режимом работы.
        /// Если система определила, что оборудование упало в offline, то режим работы уже неважен - устройство недоступно и мы не можем определить его режим работы.
        /// Если же устройство находится online, то статус оборудования определяется режимом работы.
        /// 
        /// Ещё один важный момент - статус оборудования надо хранить в БД, т.к. он потребуется для составления отчётов.
        /// 
        /// Я думаю, что надо вынести статус как отдельную сущность. 
        /// Эта сущность будет по сути отображать факт изменения статуса IMM - устройство стало offline, устройство стало online с режимом работы таким-то.
        /// Изменение статуса IMM происходит при обработке поступившего сообщения.
        /// Также надо сделать отделного воркера (на базе IHostedService), 
        /// который будет один раз в секунду выполнять проверку доступности IMM через IImmCache.
        /// Если IMM была доступна, а в момент проверки выяснилось, что таймаут получения сообщений превышен и IMM считается недоступной, 
        /// то воркер должен создать новый экземпляр статуса и сохранить его в БД.
        /// Замечание: IImmCache не хранит статус IMM, а только вычисляет, поэтому возможно стоит сделать отдельный кеш для статусов IMM.
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
                TemplateId = i.TemplateId,
                Manufacturer = i.Template.Manufacturer,
                Model = i.Template.Model,
                IsActive = i.IsActive,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        foreach (var dto in imms)
            dto.Status = _statusCache.GetStatus(dto.Id);

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
            TemplateId = imm.TemplateId,
            Manufacturer = imm.Template.Manufacturer,
            Model = imm.Template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt,
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
            TemplateId = imm.TemplateId,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt,
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

        var currentTask = await _context.Tasks
            .FirstOrDefaultAsync(t => t.ImmId == id && t.Status == Core.Enums.TaskStatus.InProgress);

        var entry = _statusCache.GetEntry(id);

        return Ok(new ImmStatusDto
        {
            ImmId = imm.Id,
            Status = entry?.Status ?? "Offline",
            CurrentTaskId = currentTask?.Id,
            CurrentMoldId = currentTask?.MoldId,
            CurrentCycleTime = 0, // TODO: Вычислить из телеметрии
            LastUpdate = entry?.SinceUtc ?? DateTime.MinValue
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
    /// Получить статистику по циклам за период
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ImmStatisticsDto>> GetImmStatistics(
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

        return Ok(statistics);
    }
}