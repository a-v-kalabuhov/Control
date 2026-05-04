using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Преобразует payload в данные сообщения - объект с показаниями датчика.
/// </summary>
public interface IDecodeTelemetryDataHandler
{
    /// <summary>
    /// Выполняет преобразование payload в объекты.
    /// </summary>
    /// <param name="context">Контекст обработки сообщения MQTT</param>
    /// <returns>Возвращает кортеж из (bool - успешность операции, MqttProcessingContext - обновленный контекст)</returns>
    Task<(bool Success, MqttProcessingContext UpdatedContext)> DecodeAsync(MqttProcessingContext context);
}
