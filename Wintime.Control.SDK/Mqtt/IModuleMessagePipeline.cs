namespace Wintime.Control.SDK.Mqtt;

/// <summary>
/// MQTT-пайплайн конкретного модуля. Диспетчер платформы выбирает нужный пайплайн
/// по topic и передаёт сообщение в него.
/// </summary>
public interface IModuleMessagePipeline
{
    string ModuleKey { get; }

    /// <summary>
    /// Возвращает true если этот модуль умеет обрабатывать сообщения с данным топиком.
    /// </summary>
    bool CanHandle(string topic);

    Task ProcessAsync(MqttMessage message, CancellationToken ct);
}
