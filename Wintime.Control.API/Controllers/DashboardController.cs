using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Dashboard;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ControlDbContext _context;

    public DashboardController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Средняя загрузка цеха за период (взвешенная по времени).
    /// Загрузка = суммарное время станков в статусе Auto или Manual
    ///            / (длительность периода × количество активных станков).
    /// </summary>
    [HttpGet("shift-utilization")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<ShiftUtilizationDto>> GetShiftUtilization(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);

        if (fromUtc >= toUtc)
            return BadRequest("from должен быть меньше to");

        var activeImmIds = await _context.Imms
            .Where(i => i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();

        if (activeImmIds.Count == 0)
            return Ok(new ShiftUtilizationDto { From = fromUtc, To = toUtc });

        var segments = await _context.ImmStatusHistory
            .Where(h => activeImmIds.Contains(h.ImmId)
                     && h.ChangedAt < toUtc
                     && (h.EndedAt == null || h.EndedAt > fromUtc))
            .Select(h => new
            {
                h.Status,
                ChangedAt = h.ChangedAt,
                EndedAt   = h.EndedAt
            })
            .ToListAsync();

        double productiveSeconds = 0;

        foreach (var seg in segments)
        {
            if (seg.Status != "Auto" && seg.Status != "Manual")
                continue;

            var start = seg.ChangedAt < fromUtc ? fromUtc : seg.ChangedAt;
            var end   = (seg.EndedAt == null || seg.EndedAt > toUtc) ? toUtc : seg.EndedAt.Value;

            if (end > start)
                productiveSeconds += (end - start).TotalSeconds;
        }

        var totalPossibleSeconds = (toUtc - fromUtc).TotalSeconds * activeImmIds.Count;
        var utilization = totalPossibleSeconds > 0
            ? Math.Round((decimal)(productiveSeconds / totalPossibleSeconds * 100), 1)
            : 0m;

        return Ok(new ShiftUtilizationDto
        {
            Utilization  = utilization,
            MachineCount = activeImmIds.Count,
            From         = fromUtc,
            To           = toUtc
        });
    }
}
