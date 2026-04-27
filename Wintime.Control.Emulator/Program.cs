using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;
using FluentValidation;
using Serilog;
using FluentValidation.AspNetCore;
using Wintime.Control.Emulator.Services;
using Refit;
using Wintime.Control.Emulator.Middleware;
using Microsoft.Extensions.Options;

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
// HttpClient для Refit-клиента с авторизацией к Wintime.Control.API
builder.Services.AddHttpClient("MainApi")
    .ConfigureHttpClient((sp, client) =>
    {
        var settings = sp.GetRequiredService<IOptions<EmulatorSettings>>().Value;
        client.BaseAddress = new Uri(settings.MainApi.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(settings.MainApi.TimeoutSec);
    })
    .AddHttpMessageHandler<AuthenticatedHttpClientHandler>();

// Refit-клиент к основному API
builder.Services.AddRefitClient<IImmApiClient>()
    .ConfigureHttpClient((sp, c) =>
    {
        // Наследуем настройки от "MainApi"
        var settings = sp.GetRequiredService<IOptions<EmulatorSettings>>().Value;
        c.BaseAddress = new Uri(settings.MainApi.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(settings.MainApi.TimeoutSec);
    })
    .AddHttpMessageHandler<AuthenticatedHttpClientHandler>();

builder.Services.AddScoped<AuthenticatedHttpClientHandler>();

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