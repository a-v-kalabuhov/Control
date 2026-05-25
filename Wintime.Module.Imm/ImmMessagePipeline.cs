using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.SDK.Mqtt;

namespace Wintime.Module.Imm;

/// <summary>
/// MQTT-пайплайн модуля ТПА. Обрабатывает сообщения из топиков control/imm/+/telemetry.
/// Последовательность: Decode → Validate → Store → UpdateImmStatus → CycleProcessing.
/// </summary>
public class ImmMessagePipeline(IServiceProvider sp)
    : IModuleMessagePipeline
{
    public string ModuleKey => "Imm";

    public bool CanHandle(string topic) =>
        topic.StartsWith("control/imm/", StringComparison.OrdinalIgnoreCase);

    public async Task ProcessAsync(MqttMessage message, CancellationToken ct)
    {
        // Создаём начальный контекст из нейтрального MqttMessage.
        // Data, Device, Template будут заполнены декодером.
        var context = new MqttProcessingContext(
            message.MessageId, message.Topic, message.Payload,
            Data: null, Device: null, Template: null);

        var decoder = sp.GetRequiredService<IDecodeTelemetryDataHandler>();
        var (success, decoded) = await decoder.DecodeAsync(context);
        if (!success)
            return;
        context = decoded;

        var validator = sp.GetRequiredService<IValidateTelemetryDataHandler>();
        var (valid, validated) = await validator.ValidateAsync(context);
        if (!valid)
            return;
        context = validated;

        await sp.GetRequiredService<IStoreTelemetryDataHandler>().SaveAsync(context);
        await sp.GetRequiredService<IUpdateImmStatusHandler>().UpdateStatusAsync(context);
        await sp.GetRequiredService<ICycleProcessingHandler>().ProcessAsync(context, ct);
    }
}
