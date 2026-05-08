using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Обработчик конвейера телеметрии, обновляющий статус ТПА
/// по данным входящего MQTT-сообщения.
/// </summary>
public interface IUpdateImmStatusHandler
{
    /// <summary>
    /// Извлекает режим работы из контекста сообщения и обновляет статус ТПА.
    /// </summary>
    /// <param name="context">Контекст обработки MQTT-сообщения с данными устройства.</param>
    Task UpdateStatusAsync(MqttProcessingContext context);
}
