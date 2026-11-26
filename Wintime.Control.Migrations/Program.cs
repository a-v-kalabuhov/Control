using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wintime.Control.Migrations;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
return await MigrationRunner.RunAsync(
    configuration,
    loggerFactory.CreateLogger("Wintime.Control.Migrations"));
