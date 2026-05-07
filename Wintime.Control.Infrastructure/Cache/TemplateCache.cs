using System.Collections.Concurrent;
using System.Text.Json;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Cache;

public sealed class TemplateCache : ITemplateCache
{
    private readonly ConcurrentDictionary<Guid, CachedTemplate> _cache = new();

    public CachedTemplate? GetById(Guid id)
        => _cache.TryGetValue(id, out var t) ? t : null;

    public void Upsert(Template template)
    {
        var cached = Parse(template);
        _cache[template.Id] = cached;
    }

    public void Remove(Guid id)
        => _cache.TryRemove(id, out _);

    public IReadOnlyList<CachedTemplate> GetAll()
        => _cache.Values.ToList();

    private static CachedTemplate Parse(Template template)
    {
        // TODO : вынести в настройки системы
        var deviceTimeout = 30;
        var sensors = new List<SensorTemplate>();

        if (!string.IsNullOrWhiteSpace(template.JsonConfig))
        {
            using var doc = JsonDocument.Parse(template.JsonConfig);
            var root = doc.RootElement;

            if (root.TryGetProperty("device_timeout_seconds", out var timeoutVal)
                && timeoutVal.TryGetInt32(out var timeout))
                deviceTimeout = timeout;

            if (root.TryGetProperty("sensors", out var sensorsVal)
                && sensorsVal.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sensorsVal.EnumerateArray())
                {
                    var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var field = s.TryGetProperty("field", out var f) ? f.GetString() ?? "" : "";
                    var type = s.TryGetProperty("type", out var t) ? t.GetString() ?? "float" : "float";
                    var threshold = s.TryGetProperty("threshold", out var th) && th.TryGetDecimal(out var thVal) ? thVal : 0m;

                    IReadOnlyList<string>? allowed = null;
                    if (s.TryGetProperty("allowed_values", out var av) && av.ValueKind == JsonValueKind.Array)
                        allowed = av.EnumerateArray().Select(x => x.GetString() ?? "").ToList();

                    sensors.Add(new SensorTemplate(name, field, type, threshold, allowed));
                }
            }
        }

        return new CachedTemplate(
            template.Id,
            template.Name,
            template.UpdatedAt,
            deviceTimeout,
            sensors.AsReadOnly()
        );
    }
}
