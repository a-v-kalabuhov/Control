namespace Wintime.Control.Infrastructure.MQTT;

/// <summary>
/// Интерфейс, определяющий контракт для сервиса работы с MQTT-брокером.
/// </summary>
public interface IMqttService
{
    /// <summary>
    /// Устанавливает соединение с MQTT-брокером.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию подключения.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Разрывает соединение с MQTT-брокером.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию отключения.</returns>
    Task DisconnectAsync();

    /// <summary>
    /// Получает состояние подключения к MQTT-брокеру.
    /// </summary>
    /// <value><c>true</c>, если клиент подключен; иначе <c>false</c>.</value>
    bool IsConnected { get; }
}