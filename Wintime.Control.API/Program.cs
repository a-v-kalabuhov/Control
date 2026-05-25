using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Services.Reports;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Auth;
using Wintime.Control.Infrastructure.MQTT;
using Wintime.Control.Shared.Settings;
using Wintime.Control.Infrastructure.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Cache;
using Wintime.Control.Infrastructure.Services;
using Wintime.Control.SDK;
using Wintime.Module.Imm;

var builder = WebApplication.CreateBuilder(args);

// Логирование (Serilog)
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Настройки (Options Pattern)
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection(MqttSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()!;

// Модули (временно хардкодим; после физического выделения — PluginLoader из plugins/)
IReadOnlyList<IAppModule> modules = [new ImmModule()];
builder.Services.AddSingleton(modules);

// База данных
builder.Services.AddDbContext<ControlDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = false; // Используем Login вместо Email
})
.AddEntityFrameworkStores<ControlDbContext>()
.AddDefaultTokenProviders()
.AddRoleManager<RoleManager<IdentityRole>>();

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = "role"
    };
});

// Authorization Policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
    .AddPolicy("ManagerOrAdmin", policy => policy.RequireRole("Manager", "Admin"))
    .AddPolicy("AdjusterOrHigher", policy => policy.RequireRole("Adjuster", "Manager", "Admin"));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsSettings.AllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = false;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CONTROL API - Управление цехом ТПА (IMM)",
        Version = "v1",
        Description = "API для системы мониторинга и управления цехом термопластавтоматов.",
        Contact = new OpenApiContact
        {
            Name = "ООО «ВИНТАЙМ»",
            Email = "wintime@wintime.pro"
        }
    });

    // JWT в Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// Demo mode: route task state changes to emulator
if (builder.Configuration.GetValue<bool>("DemoMode"))
{
    builder.Services.AddHttpClient<IEmulatorControlService, HttpEmulatorControlService>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["EmulatorSettings:BaseUrl"] ?? "http://localhost:5002");
    });
}
else
{
    builder.Services.AddSingleton<IEmulatorControlService, NoOpEmulatorControlService>();
}

// Сервисы
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
// Report Service
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IPdfReportService, PdfReportService>();
// MQTT Service
builder.Services.AddSingleton<IWintimeMqttClientFactory, WintimeMqttClientFactory>();
builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<IWintimeMqttClientFactory>();
    return factory.CreateClient();
});
// Message processing (платформенный пайплайн)
builder.Services.AddMessageProcessing();

// Регистрация сервисов каждого модуля
foreach (var module in modules)
    module.RegisterServices(builder.Services, builder.Configuration);

// In-memory caches — must be registered before MqttBackgroundService starts
builder.Services.AddSingleton<ITemplateCache, TemplateCache>();
builder.Services.AddSingleton<IImmCache, MemoryImmCache>();
builder.Services.AddHostedService<TemplateCacheStartupService>();
builder.Services.AddSingleton<IMessageProcessor, MessageProcessor>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService<MqttBackgroundService>();

var app = builder.Build();

// Pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CONTROL API v1");
    c.RoutePrefix = "swagger";
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSerilogRequestLogging();

// CORS должен быть перед HTTPS-редиректом для обработки preflight запросов
app.UseCors("AllowFrontend");

// В development среде отключаем HTTPS редирект для корректной работы preflight запросов
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

// Инициализация БД (для разработки)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    db.Database.Migrate();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    // TODO: убрать в продакшне
    string[] roleNames = { "Admin", "Manager", "Adjuster", "Observer", "Emulator" };
    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole(roleName));
    }

    var seedUsers = new[]
    {
        new { UserName = "admin",    Email = "admin@control.local",    FullName = "Администратор Системы", Role = UserRole.Admin,    RoleName = "Admin",    Password = "Admin123!"    },
        new { UserName = "manager",  Email = "manager@control.local",  FullName = "Начальник цеха",        Role = UserRole.Manager,  RoleName = "Manager",  Password = "Manager123!"  },
        new { UserName = "adjuster", Email = "adjuster@control.local", FullName = "Наладчик Тестовый",    Role = UserRole.Adjuster, RoleName = "Adjuster", Password = "Adjuster123!" },
        new { UserName = "emulator", Email = "emulator@control.local", FullName = "Эмулятор ТПА",         Role = UserRole.Emulator, RoleName = "Emulator", Password = "Emulator123!" },
    };

    foreach (var seed in seedUsers)
    {
        if (await userManager.FindByNameAsync(seed.UserName) != null)
            continue;

        var user = new User
        {
            UserName = seed.UserName,
            Email = seed.Email,
            FullName = seed.FullName,
            Role = seed.Role,
            IsActive = true
        };

        await userManager.CreateAsync(user, seed.Password);
        await userManager.AddToRoleAsync(user, seed.RoleName);
        Log.Information("Seed user created: {UserName} / {Password}", seed.UserName, seed.Password);
    }

    // Дефолтная смена 08:00–17:00, перерыв 12:00–13:00
    if (!db.Shifts.Any())
    {
        db.Shifts.Add(new Shift
        {
            StartMinutes = 480,
            DurationMinutes = 540,
            BreakStartMinutes = 720,
            BreakDurationMinutes = 60
        });
        await db.SaveChangesAsync();
        Log.Information("Создана дефолтная смена: 08:00–17:00, перерыв 12:00–13:00");
    }

    // Синхронизируем реестр модулей: каждый загруженный модуль должен иметь запись в AppModules
    foreach (var module in modules)
    {
        if (!await db.AppModules.AnyAsync(m => m.Key == module.Key))
        {
            db.AppModules.Add(new AppModuleRecord
            {
                Key = module.Key,
                IsEnabled = true,
                InstalledVersion = module.ModuleVersion.ToString(),
                EnabledAt = DateTime.UtcNow
            });
            Log.Information("Registered module in AppModules: {Key} v{Version}", module.Key, module.ModuleVersion);
        }
    }
    await db.SaveChangesAsync();
}

Log.Information("CONTROL API starting on {Urls}", string.Join(", ", app.Urls));
app.Run();

public partial class Program { }