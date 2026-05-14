using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

public interface ICycleProcessingHandler
{
    System.Threading.Tasks.Task ProcessAsync(MqttProcessingContext context, CancellationToken ct = default);
}
