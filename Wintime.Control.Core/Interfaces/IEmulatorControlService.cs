namespace Wintime.Control.Core.Interfaces;

public interface IEmulatorControlService
{
    Task SetModeAsync(string immId, string mode, CancellationToken ct = default);
}
