using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Infrastructure.Services;

public class FactoryClock : IFactoryClock
{
    private readonly ControlDbContext _context;
    private FactorySchedule? _schedule;

    public FactoryClock(ControlDbContext context) => _context = context;

    public DateTime UtcNow => DateTime.UtcNow;

    public async Task<FactorySchedule> GetScheduleAsync(CancellationToken ct = default)
    {
        if (_schedule != null)
            return _schedule;

        var first = await _context.Shifts
            .OrderBy(s => s.StartMinutes)
            .Select(s => new { s.StartMinutes, s.TimeZoneId })
            .FirstOrDefaultAsync(ct);

        var tz = TimeZoneInfo.FindSystemTimeZoneById(first?.TimeZoneId ?? FactoryCalendar.DefaultTimeZoneId);
        return _schedule = new FactorySchedule(tz, first?.StartMinutes);
    }
}
