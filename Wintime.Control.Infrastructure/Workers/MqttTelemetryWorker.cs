// Infrastructure/Workers/SensorWorker.cs
using System.Reflection.Metadata.Ecma335;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Behaviors;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Worker for processing MQTT messages.
/// Этот класс получает payload MQTT сообщения из канала и запускает pipeline обработки сообщения.
/// </summary>
public class MqttTelemetryWorker : BackgroundService
{
    private readonly ChannelReader<MqttProcessingContext> _reader;
    private readonly IServiceProvider _sp;
    private ILogger<MqttTelemetryWorker> _logger; 

    public MqttTelemetryWorker(ChannelReader<MqttProcessingContext> reader, IServiceProvider sp)
    {
        _reader = reader;
        _sp = sp;
        _logger = _sp.GetRequiredService<ILoggerFactory>().CreateLogger<MqttTelemetryWorker>();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var context in _reader.ReadAllAsync(ct))
        {
            using var scope = _sp.CreateScope();
            try
            {   var pipeline = new MessageProcessingPipeline(scope.ServiceProvider);
                await pipeline.ProcessAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process {MessageId}", context.MessageId);
            }
        }
    }
}