using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Infrastructure.Behaviors;
using Wintime.Control.SDK.Licensing;
using Wintime.Control.SDK.Mqtt;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Читает MqttMessage из bounded channel и передаёт в диспетчер модульных пайплайнов.
/// Запускается в нескольких экземплярах (ProcessorCount × 2) для параллельной обработки.
/// </summary>
public class MqttTelemetryWorker : BackgroundService
{
    private readonly ChannelReader<MqttMessage> _reader;
    private readonly IServiceProvider _sp;
    private readonly ILogger<MqttTelemetryWorker> _logger;

    public MqttTelemetryWorker(ChannelReader<MqttMessage> reader, IServiceProvider sp)
    {
        _reader = reader;
        _sp = sp;
        _logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MqttTelemetryWorker>();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var message in _reader.ReadAllAsync(ct))
        {
            using var scope = _sp.CreateScope();
            try
            {
                var pipelines = scope.ServiceProvider.GetServices<IModuleMessagePipeline>();
                var licenseCache = scope.ServiceProvider.GetRequiredService<IModuleLicenseCache>();
                var dispatchLogger = scope.ServiceProvider
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger<MessageProcessingPipeline>();

                var dispatcher = new MessageProcessingPipeline(pipelines, licenseCache, dispatchLogger);
                await dispatcher.ProcessAsync(message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {MessageId}", message.MessageId);
            }
        }
    }
}
