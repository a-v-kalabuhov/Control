using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

public class ValidateTelemetryDataHandler : IValidateTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ILogger<ValidateTelemetryDataHandler> _logger;
    
    public ValidateTelemetryDataHandler(ControlDbContext dbContext, ILogger<ValidateTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(bool, MqttProcessingContext)> ValidateAsync(MqttProcessingContext context)
    {
        _logger.LogError("Stab IValidateTelemetryDataHandler executed for context: {context}", context);
        return (true, context);  // Stub return 
    }
}