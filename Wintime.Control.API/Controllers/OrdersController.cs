using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public class OrdersController : ControllerBase
{
    private readonly ControlDbContext _context;
    public OrdersController(ControlDbContext context) => _context = context;

    // Агрегаты прогресса по набору заказов (derive-on-read, одна GroupBy — без N+1).
    private async Task<Dictionary<Guid, (int produced, int defect, int count)>> ComputeAggregatesAsync(
        IReadOnlyCollection<Guid> orderIds)
    {
        if (orderIds.Count == 0)
            return new();
        var rows = await _context.ShiftTasks
            .Where(t => t.OrderId != null && orderIds.Contains(t.OrderId.Value))
            .GroupBy(t => t.OrderId!.Value)
            .Select(g => new
            {
                OrderId = g.Key,
                Produced = g.Sum(t => t.ActualQuantity),
                Defect = g.Sum(t => t.DefectQuantity),
                Count = g.Count()
            })
            .ToListAsync();
        return rows.ToDictionary(r => r.OrderId, r => (r.Produced, r.Defect, r.Count));
    }

    private static OrderDto ToDto(Order o, (int produced, int defect, int count) agg)
    {
        var good = agg.produced - agg.defect;
        return new OrderDto
        {
            Id = o.Id,
            Number = o.Number,
            OrderDate = o.OrderDate,
            DueDate = o.DueDate,
            ProductTypeId = o.ProductTypeId,
            ProductTypeArticle = o.ProductType?.Article,
            ProductTypeName = o.ProductType?.Name,
            Quantity = o.Quantity,
            Status = o.Status,
            Note = o.Note,
            ProducedQuantity = agg.produced,
            DefectQuantity = agg.defect,
            GoodQuantity = good,
            ProgressPercent = o.Quantity > 0 ? (decimal)good / o.Quantity * 100 : 0,
            TaskCount = agg.count,
            CreatedAt = o.CreatedAt
        };
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetList(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] Guid? productTypeId = null)
    {
        var query = _context.Orders.Include(o => o.ProductType).AsQueryable();
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.Number.Contains(search));
        if (productTypeId.HasValue)
            query = query.Where(o => o.ProductTypeId == productTypeId.Value);

        var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
        var agg = await ComputeAggregatesAsync(orders.Select(o => o.Id).ToList());
        var dtos = orders.Select(o => ToDto(o, agg.GetValueOrDefault(o.Id))).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailsDto>> GetById(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.ProductType)
            .Include(o => o.Tasks).ThenInclude(t => t.Imm)
            .Include(o => o.Tasks).ThenInclude(t => t.Mold)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
            return NotFound();

        var agg = (await ComputeAggregatesAsync(new[] { id })).GetValueOrDefault(id);
        var baseDto = ToDto(order, agg);
        var details = new OrderDetailsDto
        {
            Id = baseDto.Id, Number = baseDto.Number, OrderDate = baseDto.OrderDate,
            DueDate = baseDto.DueDate, ProductTypeId = baseDto.ProductTypeId,
            ProductTypeArticle = baseDto.ProductTypeArticle, ProductTypeName = baseDto.ProductTypeName,
            Quantity = baseDto.Quantity, Status = baseDto.Status, Note = baseDto.Note,
            ProducedQuantity = baseDto.ProducedQuantity, DefectQuantity = baseDto.DefectQuantity,
            GoodQuantity = baseDto.GoodQuantity, ProgressPercent = baseDto.ProgressPercent,
            TaskCount = baseDto.TaskCount, CreatedAt = baseDto.CreatedAt,
            Tasks = order.Tasks.Select(t => new OrderTaskSummaryDto
            {
                TaskId = t.Id, ImmName = t.Imm?.Name, MoldName = t.Mold?.Name,
                PlanQuantity = t.PlanQuantity, ActualQuantity = t.ActualQuantity,
                DefectQuantity = t.DefectQuantity, Status = t.Status
            }).ToList()
        };
        return Ok(details);
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Number))
            return BadRequest("Номер заказа обязателен.");
        if (request.Quantity <= 0)
            return BadRequest("Количество должно быть больше нуля.");
        var pt = await _context.ProductTypes.FindAsync(request.ProductTypeId);
        if (pt == null || !pt.IsActive)
            return BadRequest("Указан неактивный или несуществующий тип изделия.");

        var order = new Order
        {
            Number = request.Number.Trim(),
            OrderDate = DateTime.SpecifyKind(request.OrderDate, DateTimeKind.Utc),
            DueDate = DateTime.SpecifyKind(request.DueDate, DateTimeKind.Utc),
            ProductTypeId = request.ProductTypeId,
            Quantity = request.Quantity,
            Note = request.Note,
            Status = OrderStatus.Active
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        order.ProductType = pt;
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order, (0, 0, 0)));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrderRequestDto request)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        if (request.Number != null)
        {
            if (string.IsNullOrWhiteSpace(request.Number))
                return BadRequest("Номер заказа не может быть пустым.");
            order.Number = request.Number.Trim();
        }
        if (request.Quantity.HasValue)
        {
            if (request.Quantity.Value <= 0)
                return BadRequest("Количество должно быть больше нуля.");
            order.Quantity = request.Quantity.Value;
        }
        if (request.OrderDate.HasValue)
            order.OrderDate = DateTime.SpecifyKind(request.OrderDate.Value, DateTimeKind.Utc);
        if (request.DueDate.HasValue)
            order.DueDate = DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc);
        if (request.Note != null)
            order.Note = request.Note;
        if (request.ProductTypeId.HasValue && request.ProductTypeId.Value != order.ProductTypeId)
        {
            // Смена изделия запрещена, если по заказу уже есть привязанные задания (аналогия ADR-0007).
            var hasTasks = await _context.ShiftTasks.AnyAsync(t => t.OrderId == id);
            if (hasTasks)
                return Conflict("Нельзя сменить изделие заказа: к нему привязаны задания.");
            var pt = await _context.ProductTypes.FindAsync(request.ProductTypeId.Value);
            if (pt == null || !pt.IsActive)
                return BadRequest("Указан неактивный или несуществующий тип изделия.");
            order.ProductTypeId = request.ProductTypeId.Value;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        var agg = (await ComputeAggregatesAsync(new[] { id })).GetValueOrDefault(id);
        order.Complete(agg.produced - agg.defect);   // DomainException → 400 при недостатке годных
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ выполнен" });
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        order.Cancel();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ отменён" });
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();
        order.Reopen();
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ возобновлён" });
    }
}
