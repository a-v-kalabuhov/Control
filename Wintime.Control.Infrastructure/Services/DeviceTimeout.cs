using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

/// <summary>
/// Таймаут связи ТПА (<c>device_timeout_seconds</c> шаблона) для случаев, когда <c>IImmCache</c>
/// ещё не знает ТПА — до первого MQTT-сообщения после старта API.
/// </summary>
internal static class DeviceTimeout
{
    /// <summary>Таймаут, если ТПА или его шаблон не найдены.</summary>
    public const int DefaultSeconds = 60;

    public static int ForImm(ControlDbContext db, ITemplateCache templateCache, Guid immId)
    {
        var templateId = db.Imms
            .Where(i => i.Id == immId)
            .Select(i => i.TemplateId)
            .FirstOrDefault();

        if (templateId == default)
            return DefaultSeconds;

        return templateCache.GetById(templateId)?.DeviceTimeoutSeconds ?? DefaultSeconds;
    }
}
