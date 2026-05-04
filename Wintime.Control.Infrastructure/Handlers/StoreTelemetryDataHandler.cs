using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

public class StoreTelemetryDataHandler : IStoreTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ILogger<StoreTelemetryDataHandler> _logger;
    
    public StoreTelemetryDataHandler(ControlDbContext dbContext, ILogger<StoreTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> SaveAsync(MqttProcessingContext context)
    {
        _logger.LogError("Stab IStoreTelemetryDataHandler executed for context: {context}", context);
        return true;
    }
}
