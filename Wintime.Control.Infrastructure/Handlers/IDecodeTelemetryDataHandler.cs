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
    /// <param name="context"></param>
    /// <returns>Возвращает false, если дальнейшая обработка не имеет смысла. Например, если сообщение пустое или содердит неинтерпретируемый формат payload.</returns>
    Task<bool> DecodeAsync(MqttProcessingContext context);
}
