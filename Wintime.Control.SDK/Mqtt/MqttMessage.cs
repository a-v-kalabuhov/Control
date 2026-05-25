namespace Wintime.Control.SDK.Mqtt;

/// <summary>
/// Нейтральный конверт MQTT-сообщения на уровне платформы.
/// Модульные обработчики получают этот объект и самостоятельно декодируют Payload.
/// </summary>
public record MqttMessage(Guid MessageId, string Topic, string Payload);
