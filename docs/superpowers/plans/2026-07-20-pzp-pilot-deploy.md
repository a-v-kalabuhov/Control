# PZP-03 Pilot Deploy — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Собрать turnkey-пакет для деплоя Control + коннектор USR-Modbus на ПК заказчика (ПЗП): готовые образы тарболами, `.bat`-скрипты, ПЗП-вариант compose с двойным доступом (http-дашборды + mkcert-https для планшетов), конфиг-файлы и инструкция для не-ИТ помощника.

**Architecture:** Артефакты живут в `deploy/pzp/` репозитория Control. `package/` — шаблон turnkey-папки (compose, `.env.example`, `.bat`, config, `CONFIG_GUIDE.md`). `build-and-save.ps1` собирает 4 образа (api, connector, postgres, mosquitto) и раскладывает их + пакет в `dist/`. Плюс одна правка кода API (флаг https-редиректа). Верификация — не юнит-тесты, а **прогон артефакта**: `docker compose config`, локальный подъём стека, проверка http/https и логов коннектора.

**Tech Stack:** Docker Desktop / docker compose v2, ASP.NET Core 9 (Kestrel), PostgreSQL 16, Mosquitto 2, PowerShell (сборка), Windows `.bat` (turnkey), mkcert (локальный CA).

## Global Constraints

- Спека-источник: `docs/superpowers/specs/2026-07-20-pzp-pilot-deploy-methodology-design.md` (все решения — оттуда).
- Ветка: `feature/pzp-pilot-deploy`.
- Доставка образов — **вариант B** (`docker save`/`load`), реестр не используется; на ПК заказчика `docker pull` не полагаемся.
- Коннектор — режим `Source=file`; `immId` живёт в `config/machines.json` (`[{immId, immName, connectorAlias}]`) и обязан = `Imm.Id` созданного в UI ТПА.
- Двойной доступ: дашборды `http://<IP>:5000`, планшеты `https://<IP>` (порт контейнера 8443 → хост 443) с mkcert-сертификатом; корневой CA — только на планшеты.
- Эмулятор в пилоте **не разворачивается**.
- Все `DateTime` в EF-запросах — `Kind=Utc` (правило Npgsql проекта; на деплой не влияет, но не нарушать).
- Локальные теги образов: `wintime/api:pilot`, `wintime/connector:pilot`. Базовые: `postgres:16-alpine`, `eclipse-mosquitto:2.0`.
- Env-ключи коннектора (проверено по `Program.cs`/`Settings.cs`): `Connector__Source`, `Connector__SourcePath`, `Connector__ConfigDir`, `Mqtt__BrokerHost`, `Mqtt__Port`.
- Env-ключи API: `ASPNETCORE_URLS`, `ASPNETCORE_Kestrel__Certificates__Default__Path/Password`, `Https__Redirect`, `ConnectionStrings__DefaultConnection`, `MqttSettings__BrokerUrl`, `Bootstrap__AdminPassword`.
- mkcert `-pkcs12` создаёт PFX с фиксированным паролем `changeit` → `CERT_PASSWORD=changeit`.
- Миграции применяются автоматически при старте API (`db.Database.Migrate()`), ручной EF-апдейт не нужен.

---

### Task 1: Флаг https-редиректа в API

Единственная правка кода. В Production `UseHttpsRedirection` редиректит http→https и ломает http-дашборды. Прячем за конфиг-флаг `Https:Redirect` (дефолт `true` — прежнее поведение).

**Files:**
- Modify: `Wintime.Control.API/Program.cs:220-223`

**Interfaces:**
- Produces: конфиг-ключ `Https:Redirect` (bool, дефолт `true`). Пилотный `.env` задаёт `Https__Redirect=false`.

- [ ] **Step 1: Внести правку**

Заменить блок в `Wintime.Control.API/Program.cs` (строки 220-223):

```csharp
// В development среде отключаем HTTPS редирект для корректной работы preflight запросов
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
```

на:

```csharp
// В development среде отключаем HTTPS редирект для корректной работы preflight запросов.
// В прочих средах редирект можно выключить флагом Https:Redirect (дефолт true) —
// нужно для пилота с двойным доступом http (дашборды) + https (планшеты, mkcert).
if (!app.Environment.IsDevelopment() && builder.Configuration.GetValue("Https:Redirect", true))
{
    app.UseHttpsRedirection();
}
```

- [ ] **Step 2: Собрать API**

Run: `dotnet build Wintime.Control.API -c Release`
Expected: `Build succeeded`, 0 ошибок.

- [ ] **Step 3: Коммит**

```bash
git add Wintime.Control.API/Program.cs
git commit -m "feat(api): gate HttpsRedirection behind Https:Redirect flag"
```

> Поведенческая проверка (http не редиректится при `Https__Redirect=false` в Production) выполняется в Task 7 на живом стеке — изолированно её не воспроизвести без БД и Kestrel.

---

### Task 2: Скрипт сборки и сохранения образов

Dev-side инструмент: собрать 4 образа и разложить тарболы в `dist/images/`.

**Files:**
- Create: `deploy/pzp/build-and-save.ps1`

**Interfaces:**
- Produces: `dist/images/{api,connector,postgres,mosquitto}.tar` — вход для `1-load-images.bat`.

- [ ] **Step 1: Создать скрипт**

Create `deploy/pzp/build-and-save.ps1`:

```powershell
param(
    # Путь к репозиторию коннектора относительно этого скрипта (deploy/pzp)
    [string]$ConnectorRepo = "..\..\..\Connectors\Wintime.Connector.UsrModbus",
    [string]$OutDir = ".\dist"
)
$ErrorActionPreference = "Stop"
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path   # корень репо Control
$ConnRoot    = (Resolve-Path (Join-Path $ScriptDir $ConnectorRepo)).Path

Write-Host "==> 1/5 Сборка фронта API..."
Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
npm ci
npm run build
Pop-Location

Write-Host "==> 2/5 Publish + образ API..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
docker build -f Wintime.Control.API\Dockerfile.prod -t wintime/api:pilot .
Pop-Location

Write-Host "==> 3/5 Образ коннектора..."
Push-Location $ConnRoot
docker build -f Dockerfile -t wintime/connector:pilot .
Pop-Location

Write-Host "==> 4/5 Базовые образы..."
docker pull postgres:16-alpine
docker pull eclipse-mosquitto:2.0

Write-Host "==> 5/5 docker save -> $OutDir\images ..."
$imgDir = Join-Path $ScriptDir "dist\images"
New-Item -ItemType Directory -Force $imgDir | Out-Null
docker save wintime/api:pilot        -o (Join-Path $imgDir "api.tar")
docker save wintime/connector:pilot  -o (Join-Path $imgDir "connector.tar")
docker save postgres:16-alpine       -o (Join-Path $imgDir "postgres.tar")
docker save eclipse-mosquitto:2.0    -o (Join-Path $imgDir "mosquitto.tar")

Write-Host "Готово. Тарболы в $imgDir"
```

- [ ] **Step 2: Прогнать сборку**

Run (из `deploy/pzp`): `pwsh ./build-and-save.ps1`
Expected: завершается без ошибок; в `deploy/pzp/dist/images/` четыре файла `api.tar`, `connector.tar`, `postgres.tar`, `mosquitto.tar`.

- [ ] **Step 3: Проверить round-trip загрузки**

Run: `docker image rm wintime/connector:pilot; docker load -i dist/images/connector.tar`
Expected: `Loaded image: wintime/connector:pilot`.

- [ ] **Step 4: Коммит** (сам скрипт; `dist/` — в .gitignore, Task 6)

```bash
git add deploy/pzp/build-and-save.ps1
git commit -m "build(pzp): image build-and-save script for offline delivery"
```

---

### Task 3: ПЗП docker-compose + конфиг-файлы

**Files:**
- Create: `deploy/pzp/package/docker-compose.prod.yml`
- Create: `deploy/pzp/package/.env.example`
- Create: `deploy/pzp/package/mosquitto.conf`
- Create: `deploy/pzp/package/config/machines.json`
- Create: `deploy/pzp/package/config/machine-01.json`

**Interfaces:**
- Consumes: образы `wintime/api:pilot`, `wintime/connector:pilot`, `postgres:16-alpine`, `eclipse-mosquitto:2.0` (Task 2); сертификат `certs/pzp.pfx` (Task 4).
- Produces: рабочий стек; топик `control/imm/<immId>/telemetry`.

- [ ] **Step 1: compose**

Create `deploy/pzp/package/docker-compose.prod.yml`:

```yaml
name: wtctrl-pzp

services:
  postgres:
    image: postgres:16-alpine
    container_name: wtctrl-pzp-postgres
    environment:
      POSTGRES_DB: WintimeControlDb
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    command: postgres -c shared_buffers=128MB -c max_connections=20
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d WintimeControlDb"]
      interval: 5s
      timeout: 3s
      retries: 20
    restart: unless-stopped
    mem_limit: 512m
    networks: [wtctrl]

  mosquitto:
    image: eclipse-mosquitto:2.0
    container_name: wtctrl-pzp-mosquitto
    volumes:
      - ./mosquitto.conf:/mosquitto/config/mosquitto.conf:ro
      - mosquitto_data:/mosquitto/data
    restart: unless-stopped
    mem_limit: 64m
    networks: [wtctrl]

  api:
    image: wintime/api:pilot
    container_name: wtctrl-pzp-api
    depends_on:
      postgres:
        condition: service_healthy
      mosquitto:
        condition: service_started
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: "http://+:8080;https://+:8443"
      ASPNETCORE_Kestrel__Certificates__Default__Path: /certs/pzp.pfx
      ASPNETCORE_Kestrel__Certificates__Default__Password: ${CERT_PASSWORD}
      DOTNET_gcServer: "0"
      Https__Redirect: "${HTTPS_REDIRECT}"
      ConnectionStrings__DefaultConnection: "Host=postgres;Database=WintimeControlDb;Username=postgres;Password=${DB_PASSWORD};"
      MqttSettings__BrokerUrl: mosquitto
      Bootstrap__AdminPassword: ${BOOTSTRAP_ADMIN_PASSWORD}
    ports:
      - "5000:8080"
      - "443:8443"
    volumes:
      - ./certs:/certs:ro
    restart: unless-stopped
    mem_limit: 768m
    networks: [wtctrl]

  connector:
    image: wintime/connector:pilot
    container_name: wtctrl-pzp-connector
    depends_on:
      mosquitto:
        condition: service_started
    environment:
      DOTNET_gcServer: "0"
      Connector__Source: file
      Connector__SourcePath: config/machines.json
      Connector__ConfigDir: config
      Mqtt__BrokerHost: mosquitto
      Mqtt__Port: "1883"
    volumes:
      - ./config:/app/config:ro
    restart: unless-stopped
    mem_limit: 256m
    networks: [wtctrl]

volumes:
  postgres_data:
  mosquitto_data:

networks:
  wtctrl:
    driver: bridge
```

- [ ] **Step 2: .env.example**

Create `deploy/pzp/package/.env.example`:

```dotenv
# Wintime Control — пилотный деплой (ПЗП). Заполнить и сохранить как ".env".
# Пароль PostgreSQL (любая надёжная строка)
DB_PASSWORD=change-me-db
# Пароль первого администратора (логин: admin)
BOOTSTRAP_ADMIN_PASSWORD=change-me-admin
# Пароль PFX-сертификата. У mkcert по умолчанию — changeit (не менять без нужды)
CERT_PASSWORD=changeit
# Для пилота — false (http-дашборды + https-планшеты). НЕ менять.
HTTPS_REDIRECT=false
```

- [ ] **Step 3: mosquitto.conf**

Create `deploy/pzp/package/mosquitto.conf` (как в базовом стеке):

```conf
listener 1883
allow_anonymous true
```

- [ ] **Step 4: machines.json (шаблон)**

Create `deploy/pzp/package/config/machines.json`:

```json
[
  { "immId": "REPLACE_WITH_Imm.Id_FROM_CONTROL_UI", "immName": "BM180-MT #1", "connectorAlias": "machine-01" }
]
```

- [ ] **Step 5: machine-01.json (стартовый device-конфиг)**

Скопировать пилотный сэмпл из репо коннектора как отправную точку (реальные IP/порт/UnitID проставляет Тестер на площадке):

Run: `cp ../../Connectors/Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus/samples/config/machine-01.json deploy/pzp/package/config/machine-01.json`
Expected: файл создан; содержит секции `device`/`profile`/`stateMachine`/`registers`.

- [ ] **Step 6: Валидация compose**

Создать временный `deploy/pzp/package/.env` из `.env.example` (для интерполяции), затем:

Run (из `deploy/pzp/package`): `docker compose -f docker-compose.prod.yml config`
Expected: YAML печатается без ошибок; `${...}` подставлены; сервиса `emulator` нет; порты `5000:8080` и `443:8443` присутствуют.

- [ ] **Step 7: Коммит**

```bash
git add deploy/pzp/package/docker-compose.prod.yml deploy/pzp/package/.env.example deploy/pzp/package/mosquitto.conf deploy/pzp/package/config/machines.json deploy/pzp/package/config/machine-01.json
git commit -m "feat(pzp): PZP compose variant + config templates"
```

---

### Task 4: mkcert — локальный CA и генерация сертификата

**Files:**
- Create: `deploy/pzp/package/4-make-cert.bat`
- Create: `deploy/pzp/MKCERT_SETUP.md` (заметка автору: как один раз создать CA и что положить в пакет)

**Interfaces:**
- Consumes: `mkcert.exe`, `CAROOT/` (корневой CA), кладутся в пакет автором.
- Produces: `package/certs/pzp.pfx` (монтируется в api), `package/certs/rootCA.pem` (на планшеты). PFX-пароль = `changeit`.

- [ ] **Step 1: Заметка по разовой подготовке CA**

Create `deploy/pzp/MKCERT_SETUP.md`:

```markdown
# Разовая подготовка mkcert (автор, у себя)

1. Установить mkcert (Windows): `choco install mkcert` или скачать `mkcert.exe` c
   github.com/FiloSottile/mkcert/releases.
2. Создать локальный CA: `mkcert -install` (создаёт CAROOT с rootCA.pem + rootCA-key.pem).
3. Найти CAROOT: `mkcert -CAROOT`.
4. Положить в пакет:
   - `mkcert.exe` → `package/mkcert.exe`
   - весь каталог CAROOT → `package/CAROOT/` (rootCA.pem + rootCA-key.pem)
   - публичный `rootCA.pem` → `package/certs/rootCA.pem` (для установки на планшеты)
5. `certs/pzp.pfx` НЕ кладём заранее — его генерит `4-make-cert.bat` на площадке по факт. IP.

PFX-пароль у mkcert фиксированный — `changeit`; он же в `.env` как CERT_PASSWORD.
```

- [ ] **Step 2: 4-make-cert.bat**

Create `deploy/pzp/package/4-make-cert.bat`:

```bat
@echo off
setlocal
cd /d "%~dp0"
set "IP=%~1"
if "%IP%"=="" (
  echo Usage: 4-make-cert.bat ^<LAN-IP^>   (напр. 4-make-cert.bat 192.168.1.50)
  pause & exit /b 1
)
set "CAROOT=%~dp0CAROOT"
if not exist "certs" mkdir certs
echo Генерация сертификата для %IP% ...
mkcert.exe -pkcs12 -p12-file certs\pzp.pfx %IP% 127.0.0.1 localhost
if errorlevel 1 ( echo ОШИБКА mkcert & pause & exit /b 1 )
echo.
echo Готово: certs\pzp.pfx (пароль PFX = changeit; он же CERT_PASSWORD в .env).
echo Корень для планшетов: certs\rootCA.pem
pause >nul
```

- [ ] **Step 3: Проверка генерации + https**

На dev-машине (mkcert + CAROOT на месте):

Run: `deploy\pzp\package\4-make-cert.bat 127.0.0.1`
Expected: создан `certs\pzp.pfx`.

Проверка https появится на живом стеке в Task 7 (`curl https://localhost --cacert certs/rootCA.pem`).

- [ ] **Step 4: Коммит** (скрипт и заметка; бинарь mkcert/CAROOT/certs — не в git, Task 6)

```bash
git add deploy/pzp/package/4-make-cert.bat deploy/pzp/MKCERT_SETUP.md
git commit -m "feat(pzp): mkcert cert generation script + CA setup note"
```

---

### Task 5: Turnkey `.bat`-скрипты

**Files:**
- Create: `deploy/pzp/package/1-load-images.bat`
- Create: `deploy/pzp/package/2-start.bat`
- Create: `deploy/pzp/package/3-allow-lan.bat`

- [ ] **Step 1: 1-load-images.bat**

```bat
@echo off
setlocal
cd /d "%~dp0"
echo Загрузка Docker-образов из images\ ...
for %%f in (images\*.tar) do (
  echo   %%f
  docker load -i "%%f"
  if errorlevel 1 ( echo ОШИБКА загрузки %%f & pause & exit /b 1 )
)
echo Готово. Нажмите любую клавишу.
pause >nul
```

- [ ] **Step 2: 2-start.bat**

```bat
@echo off
setlocal
cd /d "%~dp0"
if not exist ".env" ( echo Нет файла .env — скопируйте .env.example в .env и заполните. & pause & exit /b 1 )
if not exist "certs\pzp.pfx" ( echo Нет certs\pzp.pfx — сначала выполните 4-make-cert.bat. & pause & exit /b 1 )
docker compose -f docker-compose.prod.yml up -d
if errorlevel 1 ( echo ОШИБКА запуска & pause & exit /b 1 )
echo Стек запущен. Дашборд: http://localhost:5000
pause >nul
```

- [ ] **Step 3: 3-allow-lan.bat**

```bat
@echo off
:: Требует прав администратора. ПКМ -> Запуск от имени администратора.
net session >nul 2>&1
if %errorlevel% neq 0 ( echo Запустите этот файл от имени Администратора. & pause & exit /b 1 )
netsh advfirewall firewall add rule name="Wintime Control HTTP 5000" dir=in action=allow protocol=TCP localport=5000
netsh advfirewall firewall add rule name="Wintime Control HTTPS 443" dir=in action=allow protocol=TCP localport=443
echo Правила брандмауэра добавлены (TCP 5000 и 443). Нажмите любую клавишу.
pause >nul
```

- [ ] **Step 4: Проверка скриптов на dev-машине**

Run: `deploy\pzp\package\1-load-images.bat` (после Task 2 — образы в `images\`, для проверки временно скопировать/симлинкнуть `dist\images` → `package\images`)
Expected: 4 образа загружены без ошибок.

Run (от админа): `deploy\pzp\package\3-allow-lan.bat`, затем `netsh advfirewall firewall show rule name="Wintime Control HTTPS 443"`
Expected: правило показано (Enabled: Yes, LocalPort: 443).

- [ ] **Step 5: Коммит**

```bash
git add deploy/pzp/package/1-load-images.bat deploy/pzp/package/2-start.bat deploy/pzp/package/3-allow-lan.bat
git commit -m "feat(pzp): turnkey .bat scripts (load, start, firewall)"
```

---

### Task 6: CONFIG_GUIDE.md + .gitignore

**Files:**
- Create: `deploy/pzp/package/CONFIG_GUIDE.md`
- Modify: `.gitignore` (исключить `deploy/pzp/dist/`, `deploy/pzp/package/.env`, `deploy/pzp/package/certs/`, `deploy/pzp/package/CAROOT/`, `deploy/pzp/package/mkcert.exe`, `deploy/pzp/package/images/`, `deploy/pzp/publish/`)

- [ ] **Step 1: .gitignore**

Добавить в `.gitignore` (корень репо):

```gitignore
# PZP deploy — сборочные артефакты и секреты (не в git)
deploy/pzp/dist/
deploy/pzp/package/images/
deploy/pzp/package/certs/
deploy/pzp/package/CAROOT/
deploy/pzp/package/mkcert.exe
deploy/pzp/package/.env
```

- [ ] **Step 2: CONFIG_GUIDE.md**

Create `deploy/pzp/package/CONFIG_GUIDE.md`:

````markdown
# Инструкция по настройке пилота Wintime Control (ПЗП)

Помощник работает только внутри этой папки. Все команды — двойным кликом по `.bat`.
Правки текстовых файлов делает автор удалённо (или помощник под диктовку).

## Что где менять

### `.env` (пароли и параметры)
Скопировать `.env.example` → `.env`, заполнить:

| Ключ | Что вписать |
|---|---|
| `DB_PASSWORD` | любой надёжный пароль БД (латиница/цифры, без пробелов) |
| `BOOTSTRAP_ADMIN_PASSWORD` | пароль администратора (логин в системе — `admin`) |
| `CERT_PASSWORD` | оставить `changeit` (пароль сертификата mkcert по умолчанию) |
| `HTTPS_REDIRECT` | оставить `false` |

### `config/machines.json` (список ТПА)
После создания ТПА в интерфейсе (см. ниже) вписать его `Imm.Id` в поле `immId`:
```json
[ { "immId": "<GUID из карточки ТПА>", "immName": "BM180-MT #1", "connectorAlias": "machine-01" } ]
```

### `config/machine-01.json` (параметры шлюза)
Готовится Тестером на месте (реальные IP/порт/UnitID/роли/калибровка). `connectorAlias`
в `machines.json` должен совпадать с именем этого файла (`machine-01`).

## Порядок запуска

1. **`1-load-images.bat`** — загрузить образы (один раз).
2. Заполнить **`.env`** (автор).
3. **`4-make-cert.bat <LAN-IP>`** — сертификат на фактический IP ПК (автор). IP узнать: `ipconfig`
   → строка «IPv4-адрес» активного адаптера (напр. `192.168.1.50`).
4. **`2-start.bat`** — поднять стек.
5. **`3-allow-lan.bat`** — открыть порты в брандмауэре (ПКМ → **Запуск от имени администратора**).
6. Открыть на ПК `http://localhost:5000`, войти: `admin` / пароль из `.env`.
7. Справочник ТПА → создать Шаблон (тип коннектора `usr-modbus`) и ТПА → скопировать его `Imm.Id`.
8. Автор: вписать `immId` в `config/machines.json` → перезапустить коннектор
   (Docker Desktop → контейнер `wtctrl-pzp-connector` → Restart, либо `docker compose restart connector`).

## Доступ

- **Дашборды с любого ПК цеха:** `http://<LAN-IP>:5000` (сертификат не нужен).
- **Планшет наладчика (QR):** сначала установить корень доверия, потом заходить по `https://<LAN-IP>`.

### Установка корня доверия на Android-планшет
1. Перекинуть на планшет файл `certs/rootCA.pem` (почта/облако/USB).
2. Настройки → Безопасность → Шифрование и учётные данные → **Установить сертификат** →
   **Сертификат ЦС** → выбрать `rootCA.pem` → подтвердить.
3. Открыть в браузере `https://<LAN-IP>` — замок «защищено», камера/сканер QR работают.
   (Предупреждение «сеть может отслеживаться» — нормально для локального CA.)

## Если что-то не так

| Симптом | Что проверить |
|---|---|
| Дашборд по http не открывается | `2-start.bat` отработал? В `.env` `HTTPS_REDIRECT=false`? Порт 5000 в брандмауэре (`3-allow-lan`)? |
| С другой машины не открывается, с ПК — да | `3-allow-lan.bat` запущен от админа? IP верный (`ipconfig`)? |
| Планшет: камера не включается | Заходите по **https**, а не http? Корень `rootCA.pem` установлен? Сертификат выпущен на текущий IP (`4-make-cert`)? |
| ТПА не появился на дашборде | ТПА создан в UI? `immId` в `machines.json` = `Imm.Id`? Коннектор перезапущен? |
| Коннектор молчит | Docker Desktop → логи `wtctrl-pzp-connector`: есть `MQTT connected`? есть строки опроса шлюза? |
````

- [ ] **Step 3: Коммит**

```bash
git add .gitignore deploy/pzp/package/CONFIG_GUIDE.md
git commit -m "docs(pzp): operator CONFIG_GUIDE + ignore build/secret artifacts"
```

---

### Task 7: Локальный сквозной прогон и сборка пакета

Репетиция приёмки: собрать `dist/`, поднять стек локально, проверить критерии приёмки (кроме реального шлюза).

**Files:**
- Create: `deploy/pzp/ASSEMBLE.md` (как собрать `dist/` для выгрузки в облако)

- [ ] **Step 1: ASSEMBLE.md**

Create `deploy/pzp/ASSEMBLE.md`:

```markdown
# Сборка пакета для выгрузки (автор)

1. `pwsh ./build-and-save.ps1` — образы в `dist/images/`.
2. Один раз подготовить mkcert (см. MKCERT_SETUP.md) — `mkcert.exe`, `CAROOT/`,
   `certs/rootCA.pem` в `package/`.
3. Собрать выгружаемую папку: скопировать всё из `package/` + `dist/images/` → в `package/images/`.
4. Заархивировать `package/` (без `.env`, если хотите — с `.env.example`) → залить в облако.
5. Помощник качает архив, распаковывает, работает по CONFIG_GUIDE.md.
```

- [ ] **Step 2: Собрать образы и пакет**

Run: `pwsh deploy/pzp/build-and-save.ps1`
Затем скопировать `deploy/pzp/dist/images/*` → `deploy/pzp/package/images/`.
Expected: в `package/images/` — 4 тарбола.

- [ ] **Step 3: Подготовить сертификат и .env**

Run: `deploy\pzp\package\4-make-cert.bat 127.0.0.1` → `certs/pzp.pfx`.
Скопировать `.env.example` → `.env`, задать пароли (для локальной проверки — любые).

- [ ] **Step 4: Поднять стек локально**

Run (из `deploy/pzp/package`): `1-load-images.bat`, затем `2-start.bat`
Expected: `docker compose ps` — `postgres` (healthy), `mosquitto`, `api`, `connector` в статусе running; сервиса `emulator` нет.

- [ ] **Step 5: Проверить http-дашборд (без редиректа)**

Run: `curl -i http://localhost:5000/health`
Expected: `HTTP/1.1 200` (НЕ `307`/`308` на https). Это доказывает работу флага `Https__Redirect=false` (Task 1).

- [ ] **Step 6: Проверить https + вход**

Run: `curl -i https://localhost:443/health --cacert deploy/pzp/package/certs/rootCA.pem`
Expected: `HTTP/1.1 200`, TLS без ошибок доверия.
В браузере: `http://localhost:5000` открывается, вход `admin` / пароль из `.env` работает.

- [ ] **Step 7: Проверить коннектор**

Run: `docker logs wtctrl-pzp-connector`
Expected: `MQTT connected to mosquitto:1883`; попытки опроса шлюза из `machine-01.json` (реального шлюза нет → ошибки чтения/`offline` — это норма для локального прогона; важно, что коннектор стартовал, прочитал `machines.json` и подключился к брокеру).

- [ ] **Step 8: Проверить перезапуск**

Run: `docker compose -f docker-compose.prod.yml restart` затем `docker compose ps`
Expected: все сервисы снова running (эмулирует автоподъём после ребута; `restart: unless-stopped`).

- [ ] **Step 9: Погасить и зафиксировать результат**

Run: `docker compose -f docker-compose.prod.yml down`

- [ ] **Step 10: Коммит**

```bash
git add deploy/pzp/ASSEMBLE.md
git commit -m "docs(pzp): package assembly guide + local dry-run checklist"
```

---

## Проверка плана против спеки (self-review)

- **Доставка B (save/load), все 4 образа** → Task 2 (build-and-save), Task 5 (1-load).
- **Пакет-папка + turnkey .bat** → Task 3/5/6 (`package/`, .bat, CONFIG_GUIDE).
- **compose без эмулятора, +connector, image:, mem_limit, restart, dual http/https, порты 5000/443, cert-mount** → Task 3.
- **Секреты в .env + config** → Task 3 (.env.example, machines.json, machine-01.json).
- **Connector Source=file, точные env-ключи** → Task 3 (Global Constraints).
- **immId в machines.json = Imm.Id** → Task 3 (шаблон), Task 6 (шаги 7-8), Task 7.
- **mkcert: генерация + установка на планшет** → Task 4 (4-make-cert, MKCERT_SETUP), Task 6 (CONFIG_GUIDE — установка на Android).
- **LAN firewall 5000/443** → Task 5 (3-allow-lan).
- **Правка HttpsRedirection за флаг** → Task 1; проверка отсутствия редиректа → Task 7 (шаг 5).
- **Автозапуск (Docker Desktop + restart)** → Task 3 (restart), Task 7 (шаг 8).
- **Авто-миграции** → Global Constraints; фактически проверяется подъёмом api в Task 7.
- **Критерии приёмки спеки** → Task 7 (шаги 4-8) покрывают все, кроме реального шлюза (блокируется железом; полевой прогон — по TESTING_ONSITE коннектора).
- **Placeholder-скан:** плейсхолдеры `<LAN-IP>`, `<GUID>`, `REPLACE_...` — намеренные значения площадки, не TODO. Кода-заглушек нет.
- **Type/consistency:** env-ключи и теги образов согласованы между Task 2/3/5/7 (`wintime/api:pilot`, `wintime/connector:pilot`, `Https__Redirect`, `Connector__*`).
