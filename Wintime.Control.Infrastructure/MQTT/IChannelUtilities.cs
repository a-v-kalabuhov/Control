using System.Threading.Channels;

namespace Wintime.Control.Infrastructure.Mqtt;

public static class IChannelUtilities
{
    public static int Count<T>(this ChannelReader<T> reader) => 
        reader is { CanCount: true } ? reader.Count : -1;
}