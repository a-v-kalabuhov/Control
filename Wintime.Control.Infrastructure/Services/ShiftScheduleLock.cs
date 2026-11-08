using Microsoft.EntityFrameworkCore;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

internal static class ShiftScheduleLock
{
    private const long Key = 0x5754_5343_4845_4455;

    public static Task AcquireAsync(ControlDbContext context, CancellationToken ct) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({Key})", ct);
}
