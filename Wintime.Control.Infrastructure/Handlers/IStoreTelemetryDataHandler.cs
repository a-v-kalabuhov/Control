using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Сохраняемт показания датчиков в базу данных
/// </summary>
public interface IStoreTelemetryDataHandler
{
    Task<bool> SaveAsync(MqttProcessingContext context);
}

// Аналогично: IPersistHandler, IComputeHandler
