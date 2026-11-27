using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Migrations;

public static class MigrationRunner
{
    public static async Task<int> RunAsync(
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError("Connection string 'DefaultConnection' is not configured.");
            return 1;
        }

        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<ControlDbContext>(options => options.UseNpgsql(connectionString));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

            logger.LogInformation("Applying EF Core migrations.");
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("EF Core migrations applied successfully.");
            return 0;
        }
        catch (Exception exception)
        {
            // Connection string намеренно не передаётся в structured log.
            logger.LogError(exception, "EF Core migrations failed.");
            return 1;
        }
    }
}
