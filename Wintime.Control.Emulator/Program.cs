using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;
using FluentValidation;
using Serilog;
using FluentValidation.AspNetCore;
using Wintime.Control.Emulator.Services;
using Refit;
using Wintime.Control.Emulator.Middleware;

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
builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));
// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
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
builder.Services.AddSingleton<IPresetStorage, FilePresetStorage>();
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

// Раздача статики (wwwroot)
app.UseDefaultFiles(); // index.html по умолчанию
app.UseStaticFiles();
// Middleware для Vue Router
app.UseMiddleware<SpaMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Connect MQTT on startup
/// <summary>
/// Emulator uses just one MQTT connection.
/// </summary>
var mqttService = app.Services.GetRequiredService<IMqttService>();
await mqttService.ConnectAsync(CancellationToken.None);

app.Run();