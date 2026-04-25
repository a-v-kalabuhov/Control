using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;
using FluentValidation;
using Serilog;
using FluentValidation.AspNetCore;
using Wintime.Control.Emulator.Services;
using Refit;

var builder = WebApplication.CreateBuilder(args);
// Logging (Serilog)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());
// Configuraton: strongly-typed options
builder.Services.Configure<EmulatorSettings>(builder.Configuration);
// Controllers
builder.Services.AddControllers();
// Validation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<EmulationRequestValidator>();
// Services

/// <summary>
/// Services are singletons cause emulator is standalone process with in-memory state.
/// </summary>
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddSingleton<EmulationOrchestrator>();
// HTTP-client to Wintime.Control.API (Refit + authorization)
builder.Services.AddRefitClient<IImmApiClient>()
    .ConfigureHttpClient(c => 
    {
        var settings = builder.Configuration.GetSection("MainApi").Get<MainApiSettings>();
        c.BaseAddress = new Uri(settings!.BaseUrl);
    })
    .AddHttpMessageHandler<AuthenticatedHttpClientHandler>();

builder.Services.AddHttpClient<AuthenticatedHttpClientHandler>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapControllers();

// Connect MQTT on startup
/// <summary>
/// Emulator uses just one MQTT connection.
/// </summary>
var mqttService = app.Services.GetRequiredService<IMqttService>();
await mqttService.ConnectAsync(CancellationToken.None);

app.Run();