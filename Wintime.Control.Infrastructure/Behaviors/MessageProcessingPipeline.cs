using Microsoft.Extensions.Logging;
using Wintime.Control.SDK.Licensing;
using Wintime.Control.SDK.Mqtt;

namespace Wintime.Control.Infrastructure.Behaviors;

/// <summary>
/// Диспетчер MQTT-сообщений. Находит подходящий модульный пайплайн по топику,
/// проверяет состояние лицензии и делегирует обработку.
/// </summary>
public class MessageProcessingPipeline(
    IEnumerable<IModuleMessagePipeline> modulePipelines,
    IModuleLicenseCache licenseCache,
    ILogger<MessageProcessingPipeline> logger)
{
    public async Task ProcessAsync(MqttMessage message, CancellationToken ct)
    {
        var handler = modulePipelines.FirstOrDefault(p => p.CanHandle(message.Topic));
        if (handler is null)
        {
            logger.LogDebug("No module handles topic {Topic}", message.Topic);
            return;
        }

        var licenseStatus = licenseCache.GetStatus(handler.ModuleKey);
        if (!licenseStatus.LicenseValid)
        {
            logger.LogWarning(
                "Module {Key}: license invalid ({Reason}), message {MessageId} skipped",
                handler.ModuleKey, licenseStatus.InvalidReason, message.MessageId);
            return;
        }
        if (licenseStatus.EquipmentCountExceeded)
        {
            logger.LogWarning(
                "Module {Key}: equipment limit exceeded, message {MessageId} skipped",
                handler.ModuleKey, message.MessageId);
            return;
        }

        await handler.ProcessAsync(message, ct);
    }
}
