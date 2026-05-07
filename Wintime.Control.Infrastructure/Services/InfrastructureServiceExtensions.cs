using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Workers;
using Wintime.Control.Infrastructure.Handlers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessageProcessing(this IServiceCollection services)
    {
        var capacity = 25000; // Размер буфера
        var channel = Channel.CreateBounded<MqttProcessingContext>(
            new BoundedChannelOptions(capacity) 
            {
                FullMode = BoundedChannelFullMode.Wait, // Backpressure
                SingleReader = false,
                SingleWriter = false
            });
        services.AddSingleton(channel);
        // Workers
        var workerCount = Environment.ProcessorCount * 2;
        for (int i = 0; i < workerCount; i++)
        {
            services.AddHostedService(sp => new MqttTelemetryWorker(channel.Reader, sp));
        }

        return services;
    }

     public static IServiceCollection AddMessageHandlers(this IServiceCollection services)
     {
         services.AddScoped<IDecodeTelemetryDataHandler, DecodeTelemetryDataHandler>();
         services.AddScoped<IValidateTelemetryDataHandler, ValidateTelemetryDataHandler>();
         services.AddScoped<IStoreTelemetryDataHandler, StoreTelemetryDataHandler>();

         return services;
     }
}
