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

var builder = WebApplication.CreateBuilder(args);

// ========== Логирование (Serilog) ==========
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ========== Настройки (Options Pattern) ==========
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection(MqttSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()!;

// ========== База данных ==========
builder.Services.AddDbContext<ControlDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ========== Identity ==========
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

// ========== JWT Authentication ==========
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

// ========== Authorization Policies ==========
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
    .AddPolicy("ManagerOrAdmin", policy => policy.RequireRole("Manager", "Admin"))
    .AddPolicy("AdjusterOrHigher", policy => policy.RequireRole("Adjuster", "Manager", "Admin"));

// ========== CORS ==========
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

// ========== Controllers ==========
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = false;
    });

// ========== Swagger ==========
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

// ========== Сервисы ==========
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
// ========== Report Service ==========
builder.Services.AddScoped<IReportService, ReportService>();
// ========== MQTT Service ==========
builder.Services.AddSingleton<IWintimeMqttClientFactory, WintimeMqttClientFactory>();
builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<IWintimeMqttClientFactory>();
    return factory.CreateClient();
});

// Загрузка порогов из БД (для MVP — из конфига)
var thresholds = new Dictionary<string, SensorThreshold>
{
    { "temp_zone_1", new SensorThreshold { ParameterName = "temp_zone_1", ParameterType = "numeric", Threshold = 0.5m, TimeoutSeconds = 300 } },
    { "temp_zone_2", new SensorThreshold { ParameterName = "temp_zone_2", ParameterType = "numeric", Threshold = 0.5m, TimeoutSeconds = 300 } },
    { "pressure_inject", new SensorThreshold { ParameterName = "pressure_inject", ParameterType = "numeric", Threshold = 0.2m, TimeoutSeconds = 300 } },
    { "status", new SensorThreshold { ParameterName = "status", ParameterType = "discrete", Threshold = 0, TimeoutSeconds = 60 } },
    { "cycles", new SensorThreshold { ParameterName = "cycles", ParameterType = "numeric", Threshold = 1, TimeoutSeconds = 300 } }
};
builder.Services.AddSingleton(thresholds);

builder.Services.AddSingleton<ICovFilter, CovFilter>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService<MqttBackgroundService>();
builder.Services.AddMemoryCache(); // Для COV-фильтра

var app = builder.Build();

// ========== Pipeline ==========
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CONTROL API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseSerilogRequestLogging();

// CORS должен быть перед HTTPS редиректом для обработки preflight запросов
app.UseCors("AllowFrontend");

// В development среде отключаем HTTPS редирект для корректной работы preflight запросов
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ========== Инициализация БД (для разработки) ==========
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    db.Database.Migrate();

    // Создание ролей, если они еще не существуют
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    var adminEmail = "admin@control.local";
    
    // Создаем стандартные роли, если они еще не существуют
    string[] roleNames = { "Admin", "Manager", "Adjuster", "Observer" };
    foreach (var roleName in roleNames)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }
    
    // Создание админа по умолчанию (для разработки)
    if (await userManager.FindByNameAsync("admin") == null)
    {
        var admin = new User
        {
            UserName = "admin",
            Email = adminEmail,
            FullName = "Администратор Системы",
            Role = Wintime.Control.Core.Enums.UserRole.Admin,
            IsActive = true
        };
        
        await userManager.CreateAsync(admin, "Admin123!");
        await userManager.AddToRoleAsync(admin, "Admin");
        Log.Information("Admin user created: admin / Admin123!");
    }
}

Log.Information("CONTROL API starting on {Urls}", string.Join(", ", app.Urls));
app.Run();