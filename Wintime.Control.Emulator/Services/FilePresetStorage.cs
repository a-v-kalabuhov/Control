namespace Wintime.Control.Emulator.Services;

using System.Text.Json;
using Microsoft.Extensions.Options;
using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;

public class FilePresetStorage : IPresetStorage
{
    private readonly string _presetsPath;
    private readonly ILogger<FilePresetStorage> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FilePresetStorage(IOptions<StorageSettings> settings, ILogger<FilePresetStorage> logger)
    {
        _presetsPath = settings.Value.GetAbsolutePresetsPath();
        _logger = logger;
    }

    private string GetFilePath(string immId) => Path.Combine(_presetsPath, $"{immId}.json");

    public async Task<EmulationPreset?> LoadAsync(string immId, CancellationToken ct)
    {
        var path = GetFilePath(immId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<EmulationPreset>(json, _jsonOptions);
    }

    public async Task SaveAsync(EmulationPreset preset, CancellationToken ct)
    {
        preset.LastModified = DateTime.UtcNow;
        var path = GetFilePath(preset.ImmId);
        var json = JsonSerializer.Serialize(preset, _jsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
        _logger.LogInformation("Saved preset for {ImmId}", preset.ImmId);
    }

    public Task<bool> ExistsAsync(string immId, CancellationToken ct)
        => Task.FromResult(File.Exists(GetFilePath(immId)));

    public Task DeleteAsync(string immId, CancellationToken ct)
    {
        var path = GetFilePath(immId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<List<string>> ListAsync(CancellationToken ct)
    {
        var files = Directory.GetFiles(_presetsPath, "*.json");
        var ids = new List<string>();
        
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var preset = JsonSerializer.Deserialize<EmulationPreset>(json, _jsonOptions);
                if (preset?.ImmId != null)
                    ids.Add(preset.ImmId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read preset file {File}", file);
            }
        }
        
        return ids;
    }
}