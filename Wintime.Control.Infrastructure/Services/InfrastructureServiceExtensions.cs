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
         // Register handlers for processing messages
         // For decoding payload into sensor readings objects, data validation, saving to database and etc.
         // All handlers are listed in the MessageProcessingPipeline class
         services.AddScoped<IDecodeTelemetryDataHandler, DecodeTelemetryDataHandler>();
         services.AddScoped<IStoreTelemetryDataHandler, StoreTelemetryDataHandler>();
         services.AddScoped<IValidateTelemetryDataHandler, ValidateTelemetryDataHandler>();
         // Other handlers will be added here later as needed
         
         return services;
     }
}
