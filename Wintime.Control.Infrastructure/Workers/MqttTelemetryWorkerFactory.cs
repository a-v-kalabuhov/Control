using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Workers;

// DELETE : Удалить, т.к. не испольуется
public class MqttTelemetryWorkerFactory {

    private readonly ChannelReader<MqttProcessingContext> _reader;
    private readonly IServiceProvider _sp;
    public MqttTelemetryWorkerFactory(ChannelReader<MqttProcessingContext> reader, IServiceProvider sp)
    {
        _reader = reader;
        _sp = sp;
    }

    public MqttTelemetryWorker CreateWorker()
    {
        return new MqttTelemetryWorker(_reader, _sp);
    }
}