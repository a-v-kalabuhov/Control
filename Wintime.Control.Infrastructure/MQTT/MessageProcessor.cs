using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wintime.Control.SDK.Mqtt;

namespace Wintime.Control.Infrastructure.Mqtt;

public class MessageProcessor : IMessageProcessor
{
    private readonly Channel<MqttMessage> _channel;
    private readonly ILogger<MessageProcessor> _logger;

    public MessageProcessor(Channel<MqttMessage> channel, ILogger<MessageProcessor> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public int QueueLength => _channel.Reader.Count();

    public bool Enqueue(MqttMessage message)
    {
        var success = _channel.Writer.TryWrite(message);
        if (!success)
            _logger.LogWarning("Queue full, dropped message {MessageId}", message.MessageId);
        return success;
    }
}