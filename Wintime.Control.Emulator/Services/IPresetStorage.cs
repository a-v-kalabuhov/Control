using Wintime.Control.Emulator.Models;

namespace Wintime.Control.Emulator.Services;

public interface IPresetStorage
{
    Task<EmulationPreset?> LoadAsync(string immId, CancellationToken ct);
    Task SaveAsync(EmulationPreset preset, CancellationToken ct);
    Task<bool> ExistsAsync(string immId, CancellationToken ct);
    Task DeleteAsync(string immId, CancellationToken ct);
    Task<List<string>> ListAsync(CancellationToken ct);
}