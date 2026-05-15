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
// builder.Host.UseSerilog((ctx, lc) => lc
//     .ReadFrom.Configuration(ctx.Configuration)
//     .WriteTo.Console());
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
builder.Services.AddSingleton<IEmulatorMqttService, EmulatorMqttService>();
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
//app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Connect MQTT on startup
var mqttService = app.Services.GetRequiredService<IEmulatorMqttService>();
await mqttService.ConnectAsync(CancellationToken.None);

// Auto-start configured instances in Idle mode (retry until API is reachable)
var apiClient = app.Services.GetRequiredService<IImmApiClient>();
var orchestrator = app.Services.GetRequiredService<EmulationOrchestrator>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
_ = Task.Run(async () =>
{
    var delay = TimeSpan.FromSeconds(5);
    while (true)
    {
        try
        {
            var imms = await apiClient.GetImmsAsync(CancellationToken.None);
            var activeIds = imms.Where(i => i.IsActive).Select(i => i.Id);
            await orchestrator.StartAllAsync(activeIds, CancellationToken.None);
            startupLogger.LogInformation("Auto-start completed: {Count} IMM(s) started", activeIds.Count());
            break;
        }
        catch (Exception ex)
        {
            startupLogger.LogWarning("API not reachable, retrying in {Delay}s: {Message}", delay.TotalSeconds, ex.Message);
            await Task.Delay(delay);
        }
    }
});

app.Run();
