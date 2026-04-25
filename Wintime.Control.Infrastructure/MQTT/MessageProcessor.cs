using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Mqtt;

public class MessageProcessor : IMessageProcessor
{
    private readonly Channel<MqttProcessingContext> _channel;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(Channel<MqttProcessingContext> channel, ILogger<MessageProcessor> logger)
    {
        _channel = channel;     
        _logger = logger;
    }

    public int QueueLength => _channel.Reader.Count();

    public bool Enqueue(MqttProcessingContext context)
    {
        var success = _channel.Writer.TryWrite(context);
        if (!success)
            _logger.LogWarning("Queue full, dropped message {MessageId}", context.MessageId);
        return success;
    }
}