using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Проверяем изменились ли значения показаний датчиков по сравнению с предыдущим значением
/// </summary>
public interface IValidateTelemetryDataHandler
{
    Task<(bool, MqttProcessingContext)> ValidateAsync(MqttProcessingContext context);
}
