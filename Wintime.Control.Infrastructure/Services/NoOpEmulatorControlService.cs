using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Services;

public class NoOpEmulatorControlService : IEmulatorControlService
{
    public Task SetModeAsync(string immId, string mode, CancellationToken ct = default) => Task.CompletedTask;
}
