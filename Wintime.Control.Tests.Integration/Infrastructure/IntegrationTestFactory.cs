using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Integration.Infrastructure;

/// <summary>
/// Фабрика WebApplicationFactory, поднимающая реальный PostgreSQL через Testcontainers.
/// Разделяется между всеми тестами в классе через IClassFixture.
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    // Идентификаторы тестовых данных, доступных во всех тестах
    public Guid TestImmId { get; } = Guid.NewGuid();
    public Guid TestMoldId { get; } = Guid.NewGuid();
    public Guid TestTemplateId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
            // Заменяем строку подключения на Testcontainers
            var dbDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ControlDbContext>));
            if (dbDescriptor != null)
                services.Remove(dbDescriptor);

            services.AddDbContext<ControlDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Отключаем MQTT — он не нужен в интеграционных тестах
            // и без брокера упадёт при попытке подключения
            RemoveHostedServicesByName(services, "MqttBackgroundService");

            // Заменяем HTTP-клиент эмулятора на no-op: эмулятор не запущен в тестах
            var emulatorDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IEmulatorControlService));
            if (emulatorDescriptor != null)
                services.Remove(emulatorDescriptor);
            services.AddSingleton<IEmulatorControlService, NoOpEmulatorControlService>();
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Доступ к Services запускает хост: выполняются миграции и базовый сид из Program.cs
        using var scope = Services.CreateScope();

        await SeedTestUsersAsync(scope.ServiceProvider);
        await SeedTestEntitiesAsync(scope.ServiceProvider);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task SeedTestUsersAsync(IServiceProvider sp)
    {
        var userManager = sp.GetRequiredService<UserManager<User>>();

        await EnsureUserAsync(userManager, "test_manager",  "Test Manager",  "Manager123!", UserRole.Manager);
        await EnsureUserAsync(userManager, "test_adjuster", "Test Adjuster", "Adjuster123!", UserRole.Adjuster);
        await EnsureUserAsync(userManager, "test_observer", "Test Observer", "Observer123!", UserRole.Observer);
        await EnsureUserAsync(userManager, "test_inactive", "Inactive User", "Inactive123!", UserRole.Admin,
            isActive: false);
    }

    // JSON шаблона с датчиком temp (float) — используется pipeline-тестами
    public const string TestTemplateJson =
        """{"device_timeout_seconds": 30, "sensors": [{"name": "Temperature", "field": "temp", "type": "float", "threshold": 0}]}""";

    private async Task SeedTestEntitiesAsync(IServiceProvider sp)
    {
        var db            = sp.GetRequiredService<ControlDbContext>();
        var templateCache = sp.GetRequiredService<ITemplateCache>();

        Template template;
        if (!db.Templates.Any(t => t.Id == TestTemplateId))
        {
            template = new Template
            {
                Id         = TestTemplateId,
                Name       = "Test Template",
                JsonConfig = TestTemplateJson,
                IsActive   = true,
                UpdatedAt  = DateTime.UtcNow
            };
            db.Templates.Add(template);
            await db.SaveChangesAsync();
        }
        else
        {
            template = db.Templates.First(t => t.Id == TestTemplateId);
        }

        // DecodeTelemetryDataHandler ищет шаблон в ITemplateCache (singleton),
        // TemplateCacheStartupService запускается до нашего сида — добавляем вручную.
        templateCache.Upsert(template);

        if (!db.Imms.Any(i => i.Id == TestImmId))
        {
            db.Imms.Add(new Imm
            {
                Id         = TestImmId,
                Name       = "ТПА-TEST",
                TemplateId = TestTemplateId,
                IsActive   = true
            });
        }

        if (!db.Molds.Any(m => m.Id == TestMoldId))
        {
            db.Molds.Add(new Mold
            {
                Id       = TestMoldId,
                Name     = "Test Mold",
                FormId   = "TEST-001",
                Cavities = 1,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Создаёт новый ТПА с уникальным Id, привязанный к тестовому шаблону.
    /// Используется в pipeline-тестах для обеспечения изоляции: каждый тест
    /// получает собственный ImmId, чтобы записи в Telemetry и ImmStatusHistory
    /// не пересекались между тестами.
    /// </summary>
    public async Task<Guid> CreateFreshImmAsync()
    {
        var immId = Guid.NewGuid();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.Imms.Add(new Imm
        {
            Id         = immId,
            Name       = $"ТПА-{immId:N}",
            TemplateId = TestTemplateId,
            IsActive   = true
        });
        await db.SaveChangesAsync();
        return immId;
    }

    private static async Task EnsureUserAsync(
        UserManager<User> userManager,
        string userName,
        string fullName,
        string password,
        UserRole role,
        bool isActive = true)
    {
        if (await userManager.FindByNameAsync(userName) != null)
            return;

        var user = new User
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            FullName = fullName,
            Role = role,
            IsActive = isActive
        };

        await userManager.CreateAsync(user, password);
    }

    private static void RemoveHostedServicesByName(IServiceCollection services, params string[] typeNames)
    {
        var toRemove = services
            .Where(d => d.ImplementationType?.Name is { } name && typeNames.Contains(name))
            .ToList();
        foreach (var descriptor in toRemove)
            services.Remove(descriptor);
    }
}
