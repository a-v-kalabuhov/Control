# TD-06 Separate Migrations Container Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вынести применение EF Core-миграций из API в одно console application и one-shot container, который гарантированно завершается успешно до запуска API во всех поддерживаемых средах.

**Architecture:** Новый проект `Wintime.Control.Migrations` использует существующий `ControlDbContext` и предоставляет тестируемый `MigrationRunner.RunAsync`. API сохраняет только прикладные сиды; Compose связывает PostgreSQL → migrations → API через healthcheck и `service_completed_successfully`, а deploy-скрипты останавливают старый API и явно пересоздают one-shot service.

**Tech Stack:** .NET 9, EF Core 9, Npgsql, PostgreSQL 16, xUnit, Testcontainers, Docker Compose v2, PowerShell 7, Bash, Windows batch.

## Global Constraints

- Единственный компонент штатного применения схемы — `Wintime.Control.Migrations`; в `Wintime.Control.API` не добавлять fallback на `Migrate()`/`MigrateAsync()` ни в одной среде.
- Из API удалить только `db.Database.Migrate()`; демо-пользователи, production bootstrap administrator и смена по умолчанию остаются в API без изменения смысла.
- Не создавать EF-миграции, не менять схему, HTTP API, доменные инварианты и существующие migration-specific тесты, использующие `IMigrator`.
- Migrator не регистрирует Identity, MQTT, hosted services, controllers, Serilog API и доменные сиды; EF CLI в runtime-образ не входит.
- Не добавлять retry, rollback БД, backup, zero-downtime deployment, Kubernetes/Swarm job или deployment supervisor.
- Не выводить connection string и пароль в логи. Ошибка migrator возвращает ненулевой код; уже применённые миграции не откатываются.
- Общие теги образов: demo `ghcr.io/a-v-kalabuhov/control/api:<VERSION>` и `.../migrations:<VERSION>`; PZP `wintime/api:pilot` и `wintime/migrations:pilot`; smoke `wintime/api:smoke-test` и `wintime/migrations:smoke-test`.
- В demo `API_TAG` полностью заменяется на `APP_VERSION`; при публикации `latest` обновляется у обоих образов.
- Compose-охват строго: `docker-compose.dev.yml`, `deploy/demo/package/docker-compose.yml`, `deploy/pzp/package/docker-compose.prod.yml`, `deploy/smoke-test/package/docker-compose.test.yml`, `deploy/smoke-test/package/docker-compose.pzp.yml`, `deploy/smoke-test/package/docker-compose.test-report.yml`.
- `migrations` всегда имеет `restart: "no"`; API зависит от `migrations: service_completed_successfully` и не имеет прямой зависимости от PostgreSQL. Прямая зависимость API от Mosquitto сохраняется.
- Штатный update: `pull api migrations` → `stop api` → `up -d --force-recreate migrations api`; после сбоя старый API не запускать.
- Команды выполняются из корня репозитория, если шаг явно не задаёт другой каталог. Integration/Compose/Docker-проверки требуют запущенный Docker.
- Не выполнять реальный demo/PZP deploy, push в GHCR, сброс демо-БД или изменения на VPS без отдельного явного указания пользователя.
- Перед PR поднять `VERSION` с `1.37.1` до `1.38.0` (feature → minor). `master` защищён: коммиты остаются в `feature/td-06-migrations-container`, PR создаётся отдельной фазой после разрешения пользователя.

---

### Task 1: Console runner и его integration-тесты

**Files:**

- Create: `Wintime.Control.Migrations/Wintime.Control.Migrations.csproj`
- Create: `Wintime.Control.Migrations/MigrationRunner.cs`
- Create: `Wintime.Control.Migrations/Program.cs`
- Create: `Wintime.Control.Migrations/appsettings.json`
- Modify: `Wintime.Control.sln`
- Modify: `Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj`
- Test: `Wintime.Control.Tests.Integration/Migrations/MigrationRunnerTests.cs`

**Interfaces:**

- Consumes: `ControlDbContext` из `Wintime.Control.Infrastructure.Data`; строка `ConnectionStrings:DefaultConnection`.
- Produces: `public static Task<int> MigrationRunner.RunAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)`; `0` — успех/no-op, `1` — конфигурация, подключение или миграция завершились ошибкой.

- [ ] **Step 1: Создать проект и тестовую ссылку**

`Wintime.Control.Migrations/Wintime.Control.Migrations.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Wintime.Control.Infrastructure\Wintime.Control.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.15" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>
</Project>
```

`Wintime.Control.Migrations/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=WintimeControlDb;Username=postgres;Password=password;"
  }
}
```

Добавить в `Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj` вторую `ProjectReference` рядом со ссылкой на API:

```xml
<ProjectReference Include="..\Wintime.Control.Migrations\Wintime.Control.Migrations.csproj" />
```

Зарегистрировать проект:

```powershell
dotnet sln Wintime.Control.sln add Wintime.Control.Migrations/Wintime.Control.Migrations.csproj
```

- [ ] **Step 2: Написать failing integration-тесты runner**

Создать `Wintime.Control.Tests.Integration/Migrations/MigrationRunnerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Migrations;
using Xunit;

namespace Wintime.Control.Tests.Integration.Migrations;

public class MigrationRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task EmptyDatabase_AppliesAllMigrationsFromCurrentAssembly()
    {
        var result = await RunAsync(_postgres.GetConnectionString());

        result.Should().Be(0);
        await using var db = NewContext(_postgres.GetConnectionString());
        var expected = db.Database.GetMigrations().OrderBy(id => id).ToArray();
        var applied = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());
        applied.Should().Equal(expected);
    }

    [Fact]
    public async Task SecondRun_SucceedsWithoutChangingMigrationHistory()
    {
        (await RunAsync(_postgres.GetConnectionString())).Should().Be(0);
        var before = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());

        var result = await RunAsync(_postgres.GetConnectionString());

        result.Should().Be(0);
        var after = await GetAppliedMigrationsAsync(_postgres.GetConnectionString());
        after.Should().Equal(before);
    }

    [Fact]
    public async Task MissingConnectionString_ReturnsNonZero()
    {
        var configuration = new ConfigurationBuilder().Build();

        var result = await MigrationRunner.RunAsync(configuration, NullLogger.Instance);

        result.Should().NotBe(0);
    }

    [Fact]
    public async Task UnreachableDatabase_ReturnsNonZeroWithoutLongWait()
    {
        var builder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Host = "127.0.0.1",
            Port = 1,
            Timeout = 1,
            CommandTimeout = 1,
            Pooling = false
        };

        var result = await RunAsync(builder.ConnectionString);

        result.Should().NotBe(0);
    }

    private static Task<int> RunAsync(string connectionString) =>
        MigrationRunner.RunAsync(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString
                })
                .Build(),
            NullLogger.Instance);

    private static ControlDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseNpgsql(connectionString)
            .Options);

    private static async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }
}
```

- [ ] **Step 3: Убедиться, что тест падает до реализации**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj --filter FullyQualifiedName~MigrationRunnerTests
```

Expected: BUILD FAIL; в выводе есть `CS0234`/`CS0103` для отсутствующего `Wintime.Control.Migrations`/`MigrationRunner` (также допустим сопутствующий `CS5001`, потому что `Program.cs` ещё не создан).

- [ ] **Step 4: Реализовать runner и минимальную точку входа**

`Wintime.Control.Migrations/MigrationRunner.cs`:

```csharp
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
```

`Wintime.Control.Migrations/Program.cs`:

```csharp
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
```

- [ ] **Step 5: Прогнать новые тесты и сборку solution**

Run:

```powershell
dotnet test Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj --filter FullyQualifiedName~MigrationRunnerTests
dotnet build Wintime.Control.sln --configuration Release /p:SkipFrontendBuild=true
```

Expected: 4/4 runner-тестов PASS; solution BUILD SUCCEEDED, включая `Wintime.Control.Migrations`.

- [ ] **Step 6: Commit**

```powershell
git add Wintime.Control.Migrations Wintime.Control.sln Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj Wintime.Control.Tests.Integration/Migrations/MigrationRunnerTests.cs
git commit -m "feat: добавить console runner EF Core-миграций (TD-06)"
```

---

### Task 2: API больше не мигрирует БД, test factory использует runner

**Files:**

- Modify: `Wintime.Control.API/Program.cs:260-265`
- Modify: `Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs:1-72`
- Modify: `Start-DevEnv.ps1:124-134`

**Interfaces:**

- Consumes: `MigrationRunner.RunAsync(IConfiguration, ILogger, CancellationToken)` из Task 1.
- Produces: гарантия, что `WebApplicationFactory.Services` создаётся только на схеме, подготовленной тем же runner; локальный `Start-DevEnv.ps1` больше не вызывает EF CLI для применения схемы.

- [ ] **Step 1: Подготовить БД в IntegrationTestFactory до первого Services**

Добавить using:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wintime.Control.Migrations;
```

Заменить начало `InitializeAsync()` до сидов на:

```csharp
public async Task InitializeAsync()
{
    await _postgres.StartAsync();

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString()
        })
        .Build();
    var migrationResult = await MigrationRunner.RunAsync(configuration, NullLogger.Instance);
    Assert.Equal(0, migrationResult);

    // Доступ к Services запускает хост только после успешного runner:
    // Program.cs выполняет прикладной сид, но не меняет схему.
    using var scope = Services.CreateScope();

    await SeedTestUsersAsync(scope.ServiceProvider);
    await SeedTestEntitiesAsync(scope.ServiceProvider);
}
```

- [ ] **Step 2: Удалить только автомиграцию из API**

В `Wintime.Control.API/Program.cs` заменить комментарий блока и удалить строку `db.Database.Migrate();`:

```csharp
// Прикладная инициализация БД. Схему заранее применяет Wintime.Control.Migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

    var isDemoMode = builder.Configuration.GetValue<bool>("DemoMode");
```

Остальной блок демо-/bootstrap-пользователей и смены оставить без изменений.

- [ ] **Step 3: Перевести Start-DevEnv.ps1 на тот же migrator**

Заменить шаг 4 на:

```powershell
# ── 4. Применение миграций ────────────────────────────────────────────────────

Write-Step "Применение миграций БД..."
dotnet run --project "$root\Wintime.Control.Migrations" --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Применение миграций завершилось с ошибкой"
    exit 1
}
Write-Ok "Миграции применены"
```

- [ ] **Step 4: Проверить отсутствие автомиграции и прогнать integration suite**

Run:

```powershell
$apiSource = Get-Content Wintime.Control.API/Program.cs -Raw
if ($apiSource.Contains("Database.Migrate(") -or $apiSource.Contains("Database.MigrateAsync(")) { throw "API still migrates the database" }
dotnet test Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj --configuration Release /p:SkipFrontendBuild=true
```

Expected: source check не выбрасывает исключение; все integration-тесты PASS на схеме от runner, без возврата `Migrate()` в API.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/Infrastructure/IntegrationTestFactory.cs Start-DevEnv.ps1
git commit -m "refactor: убрать применение миграций из API (TD-06)"
```

---

### Task 3: Production/dev Docker-образы migrator

**Files:**

- Create: `Wintime.Control.Migrations/Dockerfile.prod`
- Create: `Wintime.Control.Migrations/Dockerfile.dev`

**Interfaces:**

- Consumes: publish output `publish/migrations` и project references из Task 1.
- Produces: image entrypoint `["dotnet", "Wintime.Control.Migrations.dll"]`, runtime user `app`; dev build не обращается к frontend.

- [ ] **Step 1: Добавить production Dockerfile**

`Wintime.Control.Migrations/Dockerfile.prod`:

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --chown=app:app publish/migrations .
USER app
ENTRYPOINT ["dotnet", "Wintime.Control.Migrations.dll"]
```

- [ ] **Step 2: Добавить self-contained dev build без Node**

`Wintime.Control.Migrations/Dockerfile.dev`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Wintime.Control.Migrations/Wintime.Control.Migrations.csproj", "Wintime.Control.Migrations/"]
COPY ["Wintime.Control.Core/Wintime.Control.Core.csproj", "Wintime.Control.Core/"]
COPY ["Wintime.Control.Infrastructure/Wintime.Control.Infrastructure.csproj", "Wintime.Control.Infrastructure/"]
COPY ["Wintime.Control.Shared/Wintime.Control.Shared.csproj", "Wintime.Control.Shared/"]
RUN dotnet restore "Wintime.Control.Migrations/Wintime.Control.Migrations.csproj"

COPY . .
WORKDIR "/src/Wintime.Control.Migrations"
RUN dotnet publish "Wintime.Control.Migrations.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "Wintime.Control.Migrations.dll"]
```

- [ ] **Step 3: Собрать и проверить оба образа**

Run:

```powershell
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
docker build -f Wintime.Control.Migrations/Dockerfile.prod -t wintime/migrations:smoke-test .
docker build -f Wintime.Control.Migrations/Dockerfile.dev -t wintime/migrations:dev .
docker image inspect wintime/migrations:smoke-test --format '{{json .Config.Entrypoint}} user={{.Config.User}}'
```

Expected: publish и оба build завершаются `0`; в migrator build нет шагов Node/npm; inspect печатает `["dotnet","Wintime.Control.Migrations.dll"] user=app`.

- [ ] **Step 4: Commit**

```powershell
git add Wintime.Control.Migrations/Dockerfile.prod Wintime.Control.Migrations/Dockerfile.dev
git commit -m "build: добавить Docker-образы migrator (TD-06)"
```

---

### Task 4: Единый Compose-контракт во всех шести средах

**Files:**

- Modify: `docker-compose.dev.yml`
- Modify: `deploy/demo/package/docker-compose.yml`
- Modify: `deploy/pzp/package/docker-compose.prod.yml`
- Modify: `deploy/smoke-test/package/docker-compose.test.yml`
- Modify: `deploy/smoke-test/package/docker-compose.pzp.yml`
- Modify: `deploy/smoke-test/package/docker-compose.test-report.yml`

**Interfaces:**

- Consumes: образы из Tasks 1–3.
- Produces: в каждом файле `migrations` ждёт `postgres: service_healthy`, а `api` ждёт `migrations: service_completed_successfully` и `mosquitto: service_started`.

- [ ] **Step 1: Обновить dev Compose**

В `docker-compose.dev.yml` добавить PostgreSQL healthcheck:

```yaml
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U postgres -d WintimeControlDb"]
      interval: 5s
      timeout: 3s
      retries: 20
```

Добавить service перед `api`:

```yaml
  migrations:
    build:
      context: .
      dockerfile: Wintime.Control.Migrations/Dockerfile.dev
    container_name: wtctrl-dev-migrations
    environment:
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=WintimeControlDb;Username=postgres;Password=password;
    depends_on:
      postgres:
        condition: service_healthy
    restart: "no"
    networks:
      - wintime-control-network
```

Заменить `api.depends_on`:

```yaml
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

- [ ] **Step 2: Обновить demo Compose**

В `deploy/demo/package/docker-compose.yml` добавить:

```yaml
  migrations:
    image: ghcr.io/a-v-kalabuhov/control/migrations:${APP_VERSION:-latest}
    container_name: wtctrl-demo-migrations
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=${DB_PASSWORD};"
    restart: "no"
    mem_limit: 256m
    logging: *logging
    networks: [wtctrl]
```

У `api` заменить image и зависимости:

```yaml
  api:
    image: ghcr.io/a-v-kalabuhov/control/api:${APP_VERSION:-latest}
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

- [ ] **Step 3: Обновить PZP Compose**

В `deploy/pzp/package/docker-compose.prod.yml` добавить:

```yaml
  migrations:
    image: wintime/migrations:pilot
    container_name: wtctrl-pzp-migrations
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=${DB_PASSWORD};"
    restart: "no"
    mem_limit: 256m
    networks: [wtctrl]
```

Заменить `api.depends_on`:

```yaml
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

- [ ] **Step 4: Обновить smoke Compose для WintimeControlDb**

В `deploy/smoke-test/package/docker-compose.test.yml` добавить:

```yaml
  migrations:
    image: wintime/migrations:smoke-test
    container_name: wtctrl-smoke-migrations
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=${DB_PASSWORD};"
    restart: "no"
    mem_limit: 256m
    networks: [wtctrl]
```

Заменить `api.depends_on` на блок из двух зависимостей:

```yaml
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

- [ ] **Step 5: Обновить smoke Compose для WintimeControl_Pzp**

В `deploy/smoke-test/package/docker-compose.pzp.yml` добавить:

```yaml
  migrations:
    image: wintime/migrations:smoke-test
    container_name: wtctrl-smoke-migrations
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControl_Pzp;Username=postgres;Password=${DB_PASSWORD};"
    restart: "no"
    mem_limit: 256m
    networks: [wtctrl]
```

Заменить `api.depends_on`:

```yaml
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

- [ ] **Step 6: Обновить report Compose и комментарий восстановления**

В `deploy/smoke-test/package/docker-compose.test-report.yml` заменить комментарий у initdb volume:

```yaml
      # Готовая БД со схемой, историей миграций и данными. После healthy PostgreSQL
      # migrations применяет только более новые миграции, затем запускается API.
```

Добавить service:

```yaml
  migrations:
    image: wintime/migrations:smoke-test
    container_name: wtctrl-smoke-migrations
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControl_ReportTest;Username=postgres;Password=${DB_PASSWORD};"
    restart: "no"
    mem_limit: 256m
    networks: [wtctrl]
```

Заменить `api.depends_on`:

```yaml
    depends_on:
      migrations:
        condition: service_completed_successfully
      mosquitto:
        condition: service_started
```

Сохранить `postgres.healthcheck.start_period: 15m` и TCP `pg_isready -h 127.0.0.1` без изменений.

- [ ] **Step 7: Автоматически проверить шесть Compose-контрактов**

Run в PowerShell:

```powershell
$envFile = Join-Path $env:TEMP "td06-compose.env"
@'
DB_PASSWORD=test-db
BOOTSTRAP_ADMIN_PASSWORD=Test-2026!
CERT_PASSWORD=test-cert
HTTPS_REDIRECT=false
APP_VERSION=1.38.0
'@ | Set-Content $envFile
$files = @(
  "docker-compose.dev.yml",
  "deploy/demo/package/docker-compose.yml",
  "deploy/pzp/package/docker-compose.prod.yml",
  "deploy/smoke-test/package/docker-compose.test.yml",
  "deploy/smoke-test/package/docker-compose.pzp.yml",
  "deploy/smoke-test/package/docker-compose.test-report.yml"
)
foreach ($file in $files) {
  $json = (docker compose --env-file $envFile -f $file config --format json | Out-String) | ConvertFrom-Json
  if ($LASTEXITCODE -ne 0) { throw "Invalid Compose: $file" }
  if ($json.services.migrations.depends_on.postgres.condition -ne "service_healthy") { throw "Postgres gate missing: $file" }
  if ($json.services.api.depends_on.migrations.condition -ne "service_completed_successfully") { throw "Migration gate missing: $file" }
  if ($json.services.api.depends_on.PSObject.Properties.Name -contains "postgres") { throw "API still depends on postgres directly: $file" }
  if ($json.services.migrations.restart -ne "no") { throw "Migrator restart policy is not no: $file" }
  if ($json.services.api.depends_on.mosquitto.condition -ne "service_started") { throw "Mosquitto dependency changed: $file" }
}
Remove-Item $envFile
"Compose contract OK: $($files.Count) files"
```

Expected: `Compose contract OK: 6 files`, ни одной ошибки разбора или контракта.

- [ ] **Step 8: Commit**

```powershell
git add docker-compose.dev.yml deploy/demo/package/docker-compose.yml deploy/pzp/package/docker-compose.prod.yml deploy/smoke-test/package/docker-compose.test.yml deploy/smoke-test/package/docker-compose.pzp.yml deploy/smoke-test/package/docker-compose.test-report.yml
git commit -m "feat: запускать API только после успешных миграций (TD-06)"
```

---

### Task 5: Demo build/deploy/reset используют два образа одной версии

**Files:**

- Modify: `deploy/demo/deploy.ps1`
- Modify: `deploy/demo/package/reset-db.sh`
- Modify: `deploy/demo/package/.env.example`
- Modify: `deploy/demo/ASSEMBLE.md`

**Interfaces:**

- Consumes: `${APP_VERSION}` и `migrations` из Task 4.
- Produces: `Build-And-Push` публикует API+migrations с одинаковыми `${Version}` и `latest`; update останавливает API перед явным пересозданием migrations+API; обе фазы reset запускают migrator.

- [ ] **Step 1: Расширить Build-And-Push**

Заменить функцию в `deploy/demo/deploy.ps1`:

```powershell
function Build-And-Push([string]$Version) {
    Write-Host "==> Сборка фронта..."
    Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
    try { npm ci; npm run build } finally { Pop-Location }
    Write-Host "==> Publish и образы API/migrations $Version..."
    Push-Location $ControlRoot
    try {
        dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
        dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
        docker build -f Wintime.Control.API\Dockerfile.prod -t "$Registry/api:$Version" -t "$Registry/api:latest" .
        docker build -f Wintime.Control.Migrations\Dockerfile.prod -t "$Registry/migrations:$Version" -t "$Registry/migrations:latest" .
    } finally { Pop-Location }
    Write-Host "==> Push в GHCR (нужен docker login ghcr.io)..."
    docker push "$Registry/api:$Version"
    docker push "$Registry/api:latest"
    docker push "$Registry/migrations:$Version"
    docker push "$Registry/migrations:latest"
}
```

Добавить `$MigrationsContainer = "wtctrl-demo-migrations"` рядом с `$ApiContainer` и сделать image inspection общим:

```powershell
function Get-RemoteImage([string]$Container) {
    $img = Invoke-Remote "docker inspect -f '{{.Config.Image}}' $Container 2>/dev/null || true"
    return "$img".Trim()
}
```

Обновить оба существующих вызова: в проверке повторной версии использовать `Get-RemoteImage $ApiContainer`; в `Wait-Site` выводить `docker compose logs --tail 100 migrations api` вместо только `api`.

- [ ] **Step 2: Заменить remote update на stop → migrate → start и проверить обе версии**

Заменить блок `pull + up -d` и последующую проверку:

```powershell
    Write-Host "==> pull, остановка API, миграции, запуск API..."
    try {
        Invoke-Remote "cd $Remote && sed -i '/^API_TAG=/d;/^APP_VERSION=/d' .env && printf '\nAPP_VERSION=%s\n' $Version >> .env && docker compose pull api migrations"
        Invoke-Remote "cd $Remote && docker compose stop api"
        Invoke-Remote "cd $Remote && docker compose up -d --force-recreate migrations api"
    } catch {
        $PSNativeCommandUseErrorActionPreference = $false
        ssh @SshOpts $Target "cd $Remote && docker compose logs --tail 100 migrations api"
        $PSNativeCommandUseErrorActionPreference = $true
        throw
    }
    Wait-Site
    $apiImage = Get-RemoteImage $ApiContainer
    if ($apiImage -ne "$Registry/api:$Version") { throw "На VPS запущен $apiImage, ожидался $Registry/api:$Version" }
    $migrationsImage = Get-RemoteImage $MigrationsContainer
    if ($migrationsImage -ne "$Registry/migrations:$Version") { throw "На VPS миграции выполнены образом $migrationsImage, ожидался $Registry/migrations:$Version" }
    Invoke-Remote "docker image prune -af" | Out-Null
```

Обновить комментарий в начале скрипта: `сборка -> push двух образов в GHCR -> пакет на VPS -> pull -> stop API -> migrations -> API -> проверка`.

- [ ] **Step 3: Пересоздавать migrator в обеих фазах reset-db.sh**

Добавить после проверки seed-файла:

```bash
start_api() {
  if ! docker compose up -d --force-recreate migrations api; then
    echo "Запуск migrations/api завершился ошибкой:" >&2
    docker compose logs --tail 100 migrations api >&2
    return 1
  fi
}
```

Заменить шаг 3 заголовком и командой:

```bash
echo "==> 3/6 Применяю миграции и запускаю API для демо-аккаунтов..."
start_api
```

Заменить шаг 6:

```bash
echo "==> 6/6 Повторно проверяю миграции и запускаю API..."
start_api
echo "Сброс БД завершён."
```

Не менять остановки API, пересоздание volume, healthcheck PostgreSQL, ожидание демо-аккаунтов и загрузку SQL-сида.

- [ ] **Step 4: Обновить demo environment и эксплуатационную документацию**

В `deploy/demo/package/.env.example` заменить API_TAG:

```dotenv
# Общий тег образов API и migrations — deploy.ps1 выставляет его при каждом деплое
APP_VERSION=latest
```

В `deploy/demo/ASSEMBLE.md`:
- в «Обновление версии» описать push `api` и `migrations` с одинаковыми `<VERSION>`/`latest`, затем `pull api migrations`, `stop api`, `up -d --force-recreate migrations api`, healthcheck и сверку обоих образов;
- в «Сброс БД» указать, что схему до и после большого сида применяет one-shot `migrations`, а демо-аккаунты создаёт API;
- заменить команду отката на:

```bash
cd ~/wtctrl-demo
sed -i '/^API_TAG=/d;/^APP_VERSION=/d' .env
printf '\nAPP_VERSION=%s\n' '<старая версия>' >> .env
docker compose pull api migrations
docker compose stop api
docker compose up -d --force-recreate migrations api
```

- добавить диагностику `docker compose ps -a`, `docker compose logs --tail 100 migrations` и правило: после ненулевого migrator не запускать старый API, поскольку часть миграций могла примениться.

- [ ] **Step 5: Проверить синтаксис без деплоя**

Run:

```powershell
[void][scriptblock]::Create((Get-Content deploy/demo/deploy.ps1 -Raw))
bash -n deploy/demo/package/reset-db.sh
$activeDemoFiles = @("deploy/demo/deploy.ps1", "deploy/demo/package/docker-compose.yml", "deploy/demo/package/.env.example", "deploy/demo/ASSEMBLE.md")
if (Select-String -Path $activeDemoFiles -Pattern "API_TAG" -SimpleMatch | Where-Object { $_.Line -notmatch "sed -i" }) { throw "Active API_TAG reference remains" }
```

Expected: PowerShell и Bash syntax checks PASS; активных использований `API_TAG`, кроме удаления старого значения из `.env`, нет. Не запускать `deploy.ps1` и не выполнять reset.

- [ ] **Step 6: Commit**

```powershell
git add deploy/demo/deploy.ps1 deploy/demo/package/reset-db.sh deploy/demo/package/.env.example deploy/demo/ASSEMBLE.md
git commit -m "deploy: упорядочить обновление демо через migrator (TD-06)"
```

---

### Task 6: PZP и smoke offline-пакеты включают migrator

**Files:**

- Modify: `deploy/pzp/build-and-save.ps1`
- Modify: `deploy/pzp/package/2-start.bat`
- Modify: `deploy/pzp/ASSEMBLE.md`
- Modify: `deploy/pzp/package/CONFIG_GUIDE.md`
- Modify: `deploy/smoke-test/build-and-save.ps1`
- Modify: `deploy/smoke-test/package/2-start.bat`
- Modify: `deploy/smoke-test/package/2-start-report.bat`
- Modify: `deploy/smoke-test/ASSEMBLE.md`
- Modify: `deploy/smoke-test/TESTING_GUIDE.md`

**Interfaces:**

- Consumes: production Dockerfile migrator и Compose services из Tasks 3–4.
- Produces: `migrations.tar` в `dist/images`; wildcard `1-load-images.bat` загружает его без изменений; start scripts останавливают API и пересоздают migrations+API.

- [ ] **Step 1: Добавить publish/build/save в PZP build script**

В `deploy/pzp/build-and-save.ps1` сделать шесть шагов. После образа API вставить:

```powershell
Write-Host "==> 3/6 Publish + образ migrations..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
docker build -f Wintime.Control.Migrations\Dockerfile.prod -t wintime/migrations:pilot .
Pop-Location
```

Перенумеровать connector/base/save как `4/6`, `5/6`, `6/6`; в save добавить сразу после `api.tar`:

```powershell
docker save wintime/migrations:pilot   -o (Join-Path $imgDir "migrations.tar")
```

- [ ] **Step 2: Добавить publish/build/save в smoke build script**

В `deploy/smoke-test/build-and-save.ps1` сделать семь шагов. После образа API вставить:

```powershell
Write-Host "==> 3/7 Publish + образ migrations..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
docker build -f Wintime.Control.Migrations\Dockerfile.prod -t wintime/migrations:smoke-test .
Pop-Location
```

Перенумеровать последующие шаги как `4/7` connector, `5/7` emulator, `6/7` base images, `7/7` save. В save добавить:

```powershell
docker save wintime/migrations:smoke-test  -o (Join-Path $imgDir "migrations.tar")
```

- [ ] **Step 3: Обновить PZP start script**

Полное содержимое командной части `deploy/pzp/package/2-start.bat` после проверок `.env` и сертификата:

```bat
docker compose -f docker-compose.prod.yml stop api
if errorlevel 1 ( echo ОШИБКА остановки API & pause & exit /b 1 )
docker compose -f docker-compose.prod.yml up -d --force-recreate migrations api connector
if errorlevel 1 (
  echo ОШИБКА миграций или запуска. API оставлен остановленным.
  docker compose -f docker-compose.prod.yml logs --tail 100 migrations api
  pause & exit /b 1
)
echo Стек запущен. Дашборд: http://localhost:5000
pause >nul
```

- [ ] **Step 4: Обновить оба smoke start script**

В `deploy/smoke-test/package/2-start.bat` заменить запуск на:

```bat
docker compose -f docker-compose.test.yml stop api
if errorlevel 1 ( echo ОШИБКА остановки API & pause & exit /b 1 )
docker compose -f docker-compose.test.yml up -d --force-recreate migrations api connector
if errorlevel 1 (
  echo ОШИБКА миграций или запуска. API оставлен остановленным.
  docker compose -f docker-compose.test.yml logs --tail 100 migrations api
  pause & exit /b 1
)
```

В `deploy/smoke-test/package/2-start-report.bat` использовать тот же поток с report-файлом:

```bat
docker compose -f docker-compose.test-report.yml stop api
if errorlevel 1 ( echo ОШИБКА остановки API & pause & exit /b 1 )
docker compose -f docker-compose.test-report.yml up -d --force-recreate migrations api connector
if errorlevel 1 (
  echo ОШИБКА миграций или запуска. API оставлен остановленным.
  docker compose -f docker-compose.test-report.yml logs --tail 100 migrations api
  pause & exit /b 1
)
```

Сохранить существующие сообщения, проверки `.env`, дампа и сертификата, `pause` и исключение интерактивного emulator.

- [ ] **Step 5: Обновить PZP/smoke инструкции**

В `deploy/pzp/ASSEMBLE.md` указать, что `dist/images/` теперь содержит `api.tar`, `migrations.tar`, `connector.tar`, `postgres.tar`, `mosquitto.tar`; `1-load-images.bat` загружает все `.tar`, поэтому отдельной команды для migrator не требуется.

В `deploy/pzp/package/CONFIG_GUIDE.md` после порядка запуска добавить:

```markdown
При каждом штатном запуске `2-start.bat` сначала останавливает API. One-shot контейнер
`migrations` применяет новые EF Core-миграции и завершается; API запускается только после
его кода `0`. При ошибке выполнить `docker compose -f docker-compose.prod.yml logs migrations`.
Старый API после ошибки не запускать: схема могла частично обновиться. `docker restart api`
не запускает миграции и не является способом обновления.
```

В `deploy/smoke-test/ASSEMBLE.md` добавить `migrations` в список образов и заменить утверждение про дамп: «при новых миграциях пересобирать дамп не обязательно — их после восстановления применит `migrations`, а не API».

В `deploy/smoke-test/TESTING_GUIDE.md` добавить `migrations` в список загружаемых образов и изменить описание `2-start.bat`/`2-start-report.bat`: PostgreSQL healthy → migrations → API → connector. Добавить команды диагностики:

```powershell
docker compose -f docker-compose.test.yml ps -a
docker compose -f docker-compose.test.yml logs --tail 100 migrations
```

Для report использовать `docker-compose.test-report.yml`. Указать, что восстановление report-дампа завершается до запуска migrator благодаря healthcheck с `start_period: 15m`.

- [ ] **Step 6: Проверить scripts без сборки offline-пакетов**

Run:

```powershell
[void][scriptblock]::Create((Get-Content deploy/pzp/build-and-save.ps1 -Raw))
[void][scriptblock]::Create((Get-Content deploy/smoke-test/build-and-save.ps1 -Raw))
$pzpText = Get-Content deploy/pzp/build-and-save.ps1 -Raw
$smokeText = Get-Content deploy/smoke-test/build-and-save.ps1 -Raw
if (-not $pzpText.Contains("wintime/migrations:pilot") -or -not $pzpText.Contains('"migrations.tar"')) { throw "PZP migrations image/save missing" }
if (-not $smokeText.Contains("wintime/migrations:smoke-test") -or -not $smokeText.Contains('"migrations.tar"')) { throw "Smoke migrations image/save missing" }
```

Expected: оба PowerShell syntax checks и обе content checks PASS. Полные build/save выполняются в Task 8, не здесь.

- [ ] **Step 7: Commit**

```powershell
git add deploy/pzp/build-and-save.ps1 deploy/pzp/package/2-start.bat deploy/pzp/ASSEMBLE.md deploy/pzp/package/CONFIG_GUIDE.md deploy/smoke-test/build-and-save.ps1 deploy/smoke-test/package/2-start.bat deploy/smoke-test/package/2-start-report.bat deploy/smoke-test/ASSEMBLE.md deploy/smoke-test/TESTING_GUIDE.md
git commit -m "deploy: включить migrator в offline-пакеты (TD-06)"
```

---

### Task 7: ADR-0033, основная документация и версия

**Files:**

- Create: `docs/adr/0033-separate-migrations-container.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `VERSION`
- Add to commit: `docs/superpowers/plans/2026-11-26-td-06-separate-migrations-container.md`

**Interfaces:**

- Consumes: реализованный runtime/deployment contract из Tasks 1–6.
- Produces: постоянное правило «схему применяет migrator», документированные команды и ADR с альтернативами/последствиями; release version `1.38.0`.

- [ ] **Step 1: Написать ADR-0033**

Создать `docs/adr/0033-separate-migrations-container.md`:

```markdown
# ADR-0033. Отдельный one-shot application/container для EF Core-миграций

- Status: Accepted
- Date: 2026-11-26
- Related: TD-06

## Контекст

`Wintime.Control.API` при старте синхронно вызывал `Database.Migrate()` вместе с прикладными
сидами. Изменение схемы было побочным эффектом запуска web-приложения, а при нескольких
экземплярах API возникала бы гонка за DDL. Для текущих одиночных Compose-установок нужен
явный порядок: PostgreSQL готов → миграции успешны → API запущен. При штатном обновлении
старый API останавливается до миграций, поскольку частично обновлённая схема может быть
несовместима со старым кодом.

## Решение

1. `Wintime.Control.Migrations` — console application .NET 9, ссылающееся на Infrastructure
   и использующее существующий `ControlDbContext`. Тестируемый runner принимает `IConfiguration`,
   вызывает `Database.MigrateAsync()` и возвращает `0` при успехе/no-op, `1` при ошибке.
2. Тот же runner используется локально, в integration test factory и в production/dev Docker-
   образах. Migrator не содержит Identity, MQTT, hosted services, controllers и доменных сидов.
3. API больше не применяет миграции ни в одной среде. Демо-пользователи, первый production
   administrator и смена по умолчанию остаются его прикладной инициализацией.
4. Во всех Compose-конфигурациях `migrations` зависит от healthy PostgreSQL и имеет
   `restart: "no"`; API зависит от `service_completed_successfully`. Exited-контейнер остаётся
   доступен для `ps -a` и `logs` до явного пересоздания.
5. Deploy останавливает старый API, затем выполняет `up -d --force-recreate migrations api`.
   API и migrations выпускаются одной версией. При сбое показываются логи, старый API не
   возобновляется и автоматический rollback не выполняется.
6. `dotnet ef migrations add` остаётся способом создания миграций. `dotnet ef database update`
   больше не является документированным штатным способом их применения.

## Альтернативы

- **`--migrate-only` в API.** Отклонено: сохраняет web-зависимости, лишнюю инициализацию и
  размытую границу ответственности ради одного образа.
- **`dotnet ef database update` в SDK-контейнере.** Отклонено: требует SDK/EF tooling в
  deployment и дополнительных project/startup-project параметров.
- **Автомиграции только в Development.** Отклонено: создаёт два механизма и риск случайного
  включения вне разработки.
- **Оставить старый API работающим при сбое.** Отклонено для одиночной установки: корректность
  потребовала бы backward-compatible expand/contract-процесса и zero-downtime deployment.

## Последствия

- Запуск и сбой схемы видны отдельно от жизненного цикла API; один и тот же код проверяется
  integration-тестами и работает в контейнере.
- Штатное обновление имеет downtime. Сбой миграции оставляет систему недоступной до исправления;
  backup и восстановление остаются отдельной эксплуатационной процедурой.
- `docker restart api` и restart policy не применяют миграции. Повторный запуск уже успешного
  one-shot service при обновлении обеспечивается явным `--force-recreate`.
- Для будущего multi-instance/zero-downtime deployment потребуется отдельное решение о блокировках
  миграций и совместимости схемы; TD-06 его не реализует.
```

- [ ] **Step 2: Обновить CLAUDE.md**

В командах заменить `dotnet ef database update` на:

```powershell
dotnet run --project Wintime.Control.Migrations  # применить миграции перед запуском API
dotnet run --project Wintime.Control.API        # API на https://localhost:7001 (схема не меняется)
```

Оставить обязательные два флага только у `dotnet ef migrations add`. Добавить отдельное правило рядом с командами:

```markdown
Схему во всех средах применяет только `Wintime.Control.Migrations` (тот же runner локально,
в integration-тестах и one-shot Docker-контейнере). API при старте не вызывает
`Migrate`/`MigrateAsync`; его демо-/bootstrap-сиды остаются. В Compose API запускается только
после `migrations: service_completed_successfully`; штатный update сначала останавливает API.
См. ADR-0033.
```

- [ ] **Step 3: Обновить README.md**

В Backend и Database migrations заменить apply-команды на:

```powershell
# Apply database migrations (required before starting API)
dotnet run --project Wintime.Control.Migrations
```

После команд добавить:

```markdown
The API never applies schema migrations. Use the same migrator locally and in Docker;
`dotnet ef migrations add` is still used to create migrations, not `dotnet ef database update`
to apply them. API startup retains only application seeds.
```

В описании первого Compose-запуска указать: PostgreSQL healthy → one-shot migrations → API application seeds. Добавить диагностику `docker compose ps -a` и `docker compose logs migrations`.

- [ ] **Step 4: Поднять VERSION**

Записать в `VERSION`:

```text
1.38.0
```

- [ ] **Step 5: Проверить документацию и commit**

Run:

```powershell
$activeDocs = @("CLAUDE.md", "README.md")
if (Select-String -Path $activeDocs -Pattern "dotnet ef database update" -SimpleMatch) { throw "EF CLI apply remains in active docs" }
if ((Get-Content VERSION -Raw).Trim() -ne "1.38.0") { throw "Unexpected VERSION" }
git add docs/adr/0033-separate-migrations-container.md docs/superpowers/plans/2026-11-26-td-06-separate-migrations-container.md CLAUDE.md README.md VERSION
git commit -m "docs: зафиксировать отдельный контейнер миграций (TD-06)"
```

Expected: active docs не содержат `dotnet ef database update`; `VERSION=1.38.0`; commit создан. Исторические specs/plans не переписывать.

---

### Task 8: Полная сборка и изолированные Compose smoke-проверки

**Files:**

- Verify only: все файлы из Tasks 1–7.
- Generated, not committed: `publish/`, `deploy/pzp/dist/images/migrations.tar`, `deploy/smoke-test/dist/images/migrations.tar`, temporary Compose JSON под `%TEMP%`.

**Interfaces:**

- Consumes: итоговый код, образы, scripts и Compose contracts.
- Produces: evidence по автоматическим тестам, одинаковым версиям образов, offline save, clean/no-op/failure Compose и report restore; исходное дерево не получает generated binaries.

- [ ] **Step 1: Прогнать весь .NET suite**

Run:

```powershell
dotnet restore Wintime.Control.sln
dotnet build Wintime.Control.sln --configuration Release --no-restore
dotnet test Wintime.Control.Tests.Unit/Wintime.Control.Tests.Unit.csproj --configuration Release --no-build
dotnet test Wintime.Control.Tests.Integration/Wintime.Control.Tests.Integration.csproj --configuration Release --no-build
```

Expected: restore/build PASS; все существующие unit и integration tests, включая 4 новых runner tests, PASS. Любой FAIL блокирует завершение; не маскировать его фильтрами.

- [ ] **Step 2: Собрать production API+migrations одной версии и offline save**

Run:

```powershell
Push-Location Wintime-Control-Frontend
try { npm ci; npm run build } finally { Pop-Location }
dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
$version = (Get-Content VERSION -Raw).Trim()
docker build -f Wintime.Control.API/Dockerfile.prod -t "wintime/api:$version" .
docker build -f Wintime.Control.Migrations/Dockerfile.prod -t "wintime/migrations:$version" .
pwsh -File deploy/pzp/build-and-save.ps1
pwsh -File deploy/smoke-test/build-and-save.ps1
if (-not (Test-Path deploy/pzp/dist/images/migrations.tar)) { throw "PZP migrations.tar missing" }
if (-not (Test-Path deploy/smoke-test/dist/images/migrations.tar)) { throw "Smoke migrations.tar missing" }
docker image inspect "wintime/api:$version" "wintime/migrations:$version" --format '{{.RepoTags}}'
```

Expected: оба production images собраны с тегом `1.38.0`; оба build-and-save завершились `0`; оба `migrations.tar` существуют. Generated tar не добавлять в git (каталоги ignored). Demo push не выполнять.

- [ ] **Step 3: Проверить clean/no-op/failure на изолированной копии dev Compose**

Создать временный resolved Compose без фиксированных имён и внешних портов, кроме API `:15000`; это не удаляет существующие dev volumes/containers:

```powershell
$project = "td06-smoke"
$tempCompose = Join-Path $env:TEMP "td06-smoke.json"
$config = (docker compose -f docker-compose.dev.yml config --format json | Out-String) | ConvertFrom-Json
foreach ($service in $config.services.PSObject.Properties) {
    $service.Value.PSObject.Properties.Remove("container_name")
    $service.Value.PSObject.Properties.Remove("ports")
}
$config.services.api.ports = @("15000:8080")
$config | ConvertTo-Json -Depth 100 | Set-Content $tempCompose

try {
    docker compose --project-directory . -p $project -f $tempCompose up -d --build migrations api
    if ($LASTEXITCODE -ne 0) { throw "Clean Compose startup failed" }

    $healthy = $false
    for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
        try {
            $response = Invoke-WebRequest http://localhost:15000/health -TimeoutSec 3 -UseBasicParsing
            $healthy = $response.StatusCode -eq 200
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw "API did not become healthy" }

    $sql = 'SELECT (SELECT count(*) FROM "Users" WHERE "UserName" = ''admin''), (SELECT count(*) FROM "Shifts");'
    $seed = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControlDb -Atc $sql | Out-String).Trim()
    if ($seed -ne "1|1") { throw "Application seeds missing: $seed" }

    $historySql = 'SELECT count(*) FROM "__EFMigrationsHistory";'
    $before = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControlDb -Atc $historySql | Out-String).Trim()
    docker compose --project-directory . -p $project -f $tempCompose stop api
    docker compose --project-directory . -p $project -f $tempCompose up -d --force-recreate migrations api
    if ($LASTEXITCODE -ne 0) { throw "Repeated migration/start failed" }
    $after = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControlDb -Atc $historySql | Out-String).Trim()
    if ($after -ne $before) { throw "No-op changed history: $before -> $after" }

    docker compose --project-directory . -p $project -f $tempCompose stop api
    $config.services.migrations.environment.ConnectionStrings__DefaultConnection = "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=wrong;"
    $config | ConvertTo-Json -Depth 100 | Set-Content $tempCompose
    docker compose --project-directory . -p $project -f $tempCompose up -d --force-recreate migrations api
    if ($LASTEXITCODE -eq 0) { throw "Broken migrator unexpectedly allowed startup" }
    $apiId = (docker compose --project-directory . -p $project -f $tempCompose ps -a -q api | Out-String).Trim()
    if ($apiId) {
        $running = (docker inspect -f '{{.State.Running}}' $apiId | Out-String).Trim()
        if ($running -eq "true") { throw "API is running after migration failure" }
    }
    docker compose --project-directory . -p $project -f $tempCompose logs --tail 50 migrations
}
finally {
    docker compose --project-directory . -p $project -f $tempCompose down -v
    Remove-Item $tempCompose -ErrorAction SilentlyContinue
}
```

Expected: clean startup даёт `/health=200`, `admin` и одну default shift; повторный запуск не меняет число истории миграций; намеренно неверный password только у migrator даёт ненулевой Compose exit, API остаётся остановленным, лог migrator доступен. `finally` удаляет только ресурсы проекта `td06-smoke`.

- [ ] **Step 4: Проверить report restore и повторный no-op**

Создать отдельный resolved report Compose с project `td06-report`, удалив `container_name` и внешние ports, назначив API `:15001:8080`; использовать тестовые env values, не реальные `.env`:

```powershell
$project = "td06-report"
$tempCompose = Join-Path $env:TEMP "td06-report.json"
$tempEnv = Join-Path $env:TEMP "td06-report.env"
@'
DB_PASSWORD=test-db
BOOTSTRAP_ADMIN_PASSWORD=Test-2026!
'@ | Set-Content $tempEnv
$config = (docker compose --env-file $tempEnv -f deploy/smoke-test/package/docker-compose.test-report.yml config --format json | Out-String) | ConvertFrom-Json
foreach ($service in $config.services.PSObject.Properties) {
    $service.Value.PSObject.Properties.Remove("container_name")
    $service.Value.PSObject.Properties.Remove("ports")
}
$config.services.api.ports = @("15001:8080")
$config | ConvertTo-Json -Depth 100 | Set-Content $tempCompose

try {
    docker compose --project-directory . -p $project -f $tempCompose up -d postgres
    $postgresId = (docker compose --project-directory . -p $project -f $tempCompose ps -a -q postgres | Out-String).Trim()
    $healthy = $false
    for ($i = 0; $i -lt 600 -and -not $healthy; $i++) {
        $state = (docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' $postgresId | Out-String).Trim()
        $healthy = $state -eq "healthy"
        if (-not $healthy) { Start-Sleep -Seconds 2 }
    }
    if (-not $healthy) { throw "Report PostgreSQL did not finish restore" }

    $historySql = 'SELECT count(*) FROM "__EFMigrationsHistory";'
    $before = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControl_ReportTest -Atc $historySql | Out-String).Trim()
    docker compose --project-directory . -p $project -f $tempCompose up -d --force-recreate migrations api
    if ($LASTEXITCODE -ne 0) { throw "Migrator/API failed after report restore" }
    $after = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControl_ReportTest -Atc $historySql | Out-String).Trim()
    if ([int]$after -lt [int]$before) { throw "Migration history was lost: $before -> $after" }

    $apiHealthy = $false
    for ($i = 0; $i -lt 60 -and -not $apiHealthy; $i++) {
        try {
            $response = Invoke-WebRequest http://localhost:15001/health -TimeoutSec 3 -UseBasicParsing
            $apiHealthy = $response.StatusCode -eq 200
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $apiHealthy) { throw "API did not become healthy on report DB" }

    docker compose --project-directory . -p $project -f $tempCompose stop api
    docker compose --project-directory . -p $project -f $tempCompose up -d --force-recreate migrations api
    if ($LASTEXITCODE -ne 0) { throw "Second migrator/API failed on report DB" }
    $repeat = (docker compose --project-directory . -p $project -f $tempCompose exec -T postgres psql -U postgres -d WintimeControl_ReportTest -Atc $historySql | Out-String).Trim()
    if ($repeat -ne $after) { throw "Report no-op changed history: $after -> $repeat" }
}
finally {
    docker compose --project-directory . -p $project -f $tempCompose down -v
    Remove-Item $tempCompose, $tempEnv -ErrorAction SilentlyContinue
}
```

Expected: migrator не стартует до healthy PostgreSQL после восстановления; итоговая история не уменьшается и содержит актуальную схему; повторный migrator не меняет историю; API стартует. Полный demo seed/reset на VPS не выполнять.

- [ ] **Step 5: Проверить reset-db.sh с малым сидом на изолированном Linux Docker host**

Выполнить на временном Linux/WSL host с Docker, .NET 9 SDK, Node.js, `openssl`, `curl` и `flock`, из корня итогового исходного дерева. Скрипт намеренно отказывается работать при любом существующем `wtctrl-demo-*` container/volume; это защита от запуска на реальном демо или занятой машине. Вместо 45–60-минутного сида используется `SELECT 1`, но сам `reset-db.sh` и обе фазы migrations/API выполняются без изменений:

```bash
(
  set -euo pipefail
  [ "$(uname -s)" = "Linux" ]
  for tool in docker dotnet npm openssl curl flock; do command -v "$tool" >/dev/null; done
  if docker ps -a --format '{{.Names}}' | grep -Eq '^wtctrl-demo-(api|postgres|migrations|mosquitto)$'; then
    echo "Host is not isolated: wtctrl-demo containers exist" >&2
    exit 1
  fi
  if docker volume inspect wtctrl-demo_postgres_data >/dev/null 2>&1; then
    echo "Host is not isolated: wtctrl-demo_postgres_data exists" >&2
    exit 1
  fi

  root=$(pwd)
  work=$(mktemp -d)
  cleanup() {
    (cd "$work" && docker compose down -v) || true
    docker volume rm -f wtctrl-demo_postgres_data >/dev/null 2>&1 || true
    rm -rf "$work"
  }
  trap cleanup EXIT

  npm ci --prefix Wintime-Control-Frontend
  npm run build --prefix Wintime-Control-Frontend
  dotnet publish Wintime.Control.API -c Release -o ./publish/api /p:UseAppHost=false /p:SkipFrontendBuild=true
  dotnet publish Wintime.Control.Migrations -c Release -o ./publish/migrations /p:UseAppHost=false
  docker build -f Wintime.Control.API/Dockerfile.prod -t ghcr.io/a-v-kalabuhov/control/api:local .
  docker build -f Wintime.Control.Migrations/Dockerfile.prod -t ghcr.io/a-v-kalabuhov/control/migrations:local .

  cp deploy/demo/package/docker-compose.yml deploy/demo/package/mosquitto.conf deploy/demo/package/reset-db.sh "$work/"
  mkdir -p "$work/certs"
  openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=localhost' \
    -keyout "$work/certs/privkey.pem" -out "$work/certs/fullchain.pem"
  chmod 644 "$work/certs/fullchain.pem" "$work/certs/privkey.pem"
  printf 'DB_PASSWORD=testdb\nAPP_VERSION=local\nHTTP_PORT=18080\nHTTPS_PORT=18443\n' > "$work/.env"
  printf 'SELECT 1;\n' > "$work/seed_demo.sql"

  cd "$work"
  bash reset-db.sh
  healthy=0
  for attempt in {1..45}; do
    if curl -k --fail --max-time 10 https://localhost:18443/health; then
      healthy=1
      break
    fi
    if [ "$attempt" -lt 45 ]; then
      sleep 2
    fi
  done
  [ "$healthy" = "1" ]
  [ "$(docker inspect -f '{{.State.ExitCode}}' wtctrl-demo-migrations)" = "0" ]
  [ "$(docker inspect -f '{{.Config.Image}}' wtctrl-demo-migrations)" = "ghcr.io/a-v-kalabuhov/control/migrations:local" ]
  [ "$(docker inspect -f '{{.Config.Image}}' wtctrl-demo-api)" = "ghcr.io/a-v-kalabuhov/control/api:local" ]
  seed=$(docker exec wtctrl-demo-postgres psql -U postgres -d WintimeControlDb -Atc \
    "SELECT (SELECT count(*) FROM \"Users\" WHERE \"UserName\"='adjuster'), (SELECT count(*) FROM \"Shifts\");")
  [ "$seed" = "1|1" ]
  cd "$root"
)
```

Expected: в выводе есть `Сброс БД завершён.`; первая фаза создала схему и демо-аккаунты, малый SQL применён, вторая фаза выполнила successful no-op; готовность `/health` ожидается ограниченным циклом 45×2 с, поскольку `docker compose up -d` не является гейтом готовности API, немедленный `curl` не требуется; `adjuster` и default shift существуют, API и migrations используют общий тег `local`. Trap удаляет только созданные на изолированном host ресурсы. Не запускать этот шаг на demo VPS.

- [ ] **Step 6: Финальный source/artifact audit**

Run:

```powershell
$apiSource = Get-Content Wintime.Control.API/Program.cs -Raw
if ($apiSource.Contains("Database.Migrate(") -or $apiSource.Contains("Database.MigrateAsync(")) { throw "API migration call returned" }
$composeFiles = @(
  "docker-compose.dev.yml",
  "deploy/demo/package/docker-compose.yml",
  "deploy/pzp/package/docker-compose.prod.yml",
  "deploy/smoke-test/package/docker-compose.test.yml",
  "deploy/smoke-test/package/docker-compose.pzp.yml",
  "deploy/smoke-test/package/docker-compose.test-report.yml"
)
foreach ($file in $composeFiles) {
  if (-not (Select-String -Path $file -Pattern "condition: service_completed_successfully" -SimpleMatch -Quiet)) { throw "Missing migration gate: $file" }
}
git status --short
```

Expected: API не содержит стартовых вызовов миграций; 6/6 Compose имеют gate; `git status` показывает только исходные файлы Tasks 1–7 (после их commits — чистое дерево), без `publish/`, `.env`, временных Compose и generated tar. Не утверждать о реальном demo deploy/reset: они не входили в проверку. Далее — code review и PR по отдельному указанию пользователя; после PR обновить TD-06 ссылками на PR/ADR-0033 и перенести задачу в архив беклога обычным процессом.
