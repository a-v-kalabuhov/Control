using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Проверяем изменились ли значения показаний датчиков
/// </summary>
public interface IValidateTelemetryDataHandler
{
    Task<bool> ValidateAsync(MqttProcessingContext context);
}
