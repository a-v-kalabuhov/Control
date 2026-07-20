using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Reports;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Auth;
using Wintime.Control.Infrastructure.MQTT;
using Wintime.Control.Shared.Settings;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Cache;
using Wintime.Control.Infrastructure.Services;

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
builder.Services.Configure<DowntimeSettings>(builder.Configuration.GetSection(DowntimeSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()!;

// База данных
builder.Services.AddDbContext<ControlDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<User, IdentityRole>(options =>
{
    // Политика паролей для производственной MES-системы. Демо-аккаунты
    // (Admin123! и т.п.) этим требованиям удовлетворяют.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 1;
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
    .AddPolicy("AdjusterOrHigher", policy => policy.RequireRole("Adjuster", "Manager", "Admin"))
    // Заглушка под РОСОМС (ROS-03): пока не привязана ни к одному endpoint. См. UserRole.Operator.
    .AddPolicy("OperatorOrHigher", policy => policy.RequireRole("Operator", "Adjuster", "Manager", "Admin"));

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

builder.Services.AddHealthChecks();

// Маппинг доменных исключений (DomainException) в HTTP 400
builder.Services.AddExceptionHandler<Wintime.Control.API.ExceptionHandling.DomainExceptionHandler>();
builder.Services.AddProblemDetails();

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
builder.Services.AddSingleton<IRefreshTokenStore, MemoryRefreshTokenStore>();
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
// Message processing
builder.Services.AddMessageProcessing();
builder.Services.AddMessageHandlers();
builder.Services.AddImmStatusTracking();

// In-memory caches — must be registered before MqttBackgroundService starts
builder.Services.AddSingleton<ITemplateCache, TemplateCache>();
builder.Services.AddSingleton<IImmCache, MemoryImmCache>();
builder.Services.AddHostedService<TemplateCacheStartupService>();
builder.Services.AddImmStatusWorkers();
builder.Services.AddSingleton<IMessageProcessor, MessageProcessor>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService<MqttBackgroundService>();

var app = builder.Build();

// Pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CONTROL API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSerilogRequestLogging();

// CORS должен быть перед HTTPS-редиректом для обработки preflight запросов
app.UseCors("AllowFrontend");

// В development среде отключаем HTTPS редирект для корректной работы preflight запросов.
// В прочих средах редирект можно выключить флагом Https:Redirect (дефолт true) —
// нужно для пилота с двойным доступом http (дашборды) + https (планшеты, mkcert).
if (!app.Environment.IsDevelopment() && builder.Configuration.GetValue("Https:Redirect", true))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();
app.MapFallbackToFile("index.html");

// Инициализация БД (для разработки)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    db.Database.Migrate();

    var isDemoMode = builder.Configuration.GetValue<bool>("DemoMode");

    // Единственный источник правды по роли — поле User.Role (enum). Identity-роли
    // (AspNetRoles/AspNetUserRoles) больше не используются: авторизация строится из
    // JWT-клейма "role", который берётся из User.Role. См. ADR-0004.

    // Демо-аккаунты создаются ТОЛЬКО при DemoMode=true.
    // Их пароли публичны (отображаются на экране входа демо-версии) и имеют полные
    // права — поэтому они никогда не должны существовать в production-инсталляции,
    // где DemoMode=false. Ответственность за корректное значение флага лежит на
    // конфигурации окружения (appsettings / env-переменные).
    if (isDemoMode)
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var seedUsers = new[]
        {
            new { UserName = "admin",    Email = "admin@control.local",    FullName = "Администратор Системы", Role = UserRole.Admin,    Password = "Admin123!"    },
            new { UserName = "manager",  Email = "manager@control.local",  FullName = "Начальник цеха",        Role = UserRole.Manager,  Password = "Manager123!"  },
            new { UserName = "adjuster", Email = "adjuster@control.local", FullName = "Наладчик Тестовый",    Role = UserRole.Adjuster, Password = "Adjuster123!" },
            new { UserName = "emulator", Email = "emulator@control.local", FullName = "Эмулятор ТПА",         Role = UserRole.Emulator, Password = "Emulator123!" },
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
            Log.Information("Demo user created: {UserName} ({Role})", seed.UserName, seed.Role);
        }

        Log.Warning("DemoMode is ON: public demo accounts with full privileges are active. " +
                    "Do NOT use this configuration in production.");
    }
    else
    {
        // Production: гарантируем наличие хотя бы одного администратора. Пароль —
        // только из конфигурации/секрета (Bootstrap:AdminPassword), никогда из
        // исходников. Создаём идемпотентно: лишь если ни одного админа ещё нет.
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        if (!await userManager.Users.AnyAsync(u => u.Role == UserRole.Admin))
        {
            var adminLogin = builder.Configuration["Bootstrap:AdminLogin"] ?? "admin";
            var adminPassword = builder.Configuration["Bootstrap:AdminPassword"];

            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                Log.Warning("No administrator exists and Bootstrap:AdminPassword is not configured. " +
                            "Set Bootstrap__AdminPassword (env/secret) to create the initial administrator.");
            }
            else
            {
                var admin = new User
                {
                    UserName = adminLogin,
                    Email = $"{adminLogin}@control.local",
                    FullName = "Администратор",
                    Role = UserRole.Admin,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(admin, adminPassword);
                if (result.Succeeded)
                    Log.Information("Bootstrap administrator '{Login}' created.", adminLogin);
                else
                    Log.Error("Failed to create bootstrap administrator: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
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
}

// Защита от случайной публикации со статическим demo-ключом коннектора.
// Дефолтный "change-me-before-deployment" не должен попадать в production —
// иначе любой, кто прочитает appsettings.json в репозитории, получит доступ
// к машинным данным через /api/connectors/...
if (!app.Environment.IsDevelopment())
{
    var connectorKey = builder.Configuration["ConnectorApiKey"];
    if (string.IsNullOrEmpty(connectorKey) || connectorKey == "change-me-before-deployment")
    {
        Log.Warning("ConnectorApiKey is empty or has the default placeholder value in a non-Development " +
                    "environment. /api/connectors endpoints are effectively unauthenticated. " +
                    "Set a strong unique ConnectorApiKey before deployment.");
    }
}

Log.Information("CONTROL API starting on {Urls}", string.Join(", ", app.Urls));
app.Run();

public partial class Program { }