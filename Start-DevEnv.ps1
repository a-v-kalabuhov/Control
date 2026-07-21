#Requires -Version 7
<#
.SYNOPSIS
    Запуск dev-окружения Wintime Control.
    Проверяет Docker-контейнеры, собирает решение, запускает API и Emulator,
    автоматически стартует IMM-инстансы из сохранённых пресетов.

.PARAMETER SkipBuild
    Пропустить dotnet build (использовать существующие бинарники).

.PARAMETER Dev
    Запустить npm run dev для разработки фронтенда (браузер откроется на :3000).
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$Dev,
    # IP(-и) хоста для HTTPS-сертификата и подсказки доступа с планшета.
    # Не задан — определяются автоматически (интерфейсы с default gateway).
    [string[]]$HostIp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ── Вспомогательные функции ───────────────────────────────────────────────────

function Write-Step([string]$msg) {
    Write-Host "`n[$(Get-Date -Format 'HH:mm:ss')] $msg" -ForegroundColor Cyan
}

function Write-Ok([string]$msg) {
    Write-Host "  ✓  $msg" -ForegroundColor Green
}

function Write-Warn([string]$msg) {
    Write-Host "  !  $msg" -ForegroundColor Yellow
}

function Write-Fail([string]$msg) {
    Write-Host "  ✗  $msg" -ForegroundColor Red
}

function Test-DockerContainer([string]$name) {
    $result = docker inspect --format '{{.State.Running}}' $name 2>$null
    return ($LASTEXITCODE -eq 0) -and ($result -eq 'true')
}

function Wait-ForService([string]$url, [int]$timeoutSec = 60) {
    $deadline = [DateTime]::Now.AddSeconds($timeoutSec)
    while ([DateTime]::Now -lt $deadline) {
        try {
            $null = Invoke-RestMethod $url -TimeoutSec 3 -ErrorAction Stop
            return $true
        } catch {
            Start-Sleep -Seconds 2
        }
    }
    return $false
}

# ── 1. Проверка Docker-инфраструктуры ─────────────────────────────────────────

Write-Step "Проверка Docker-инфраструктуры..."

$missingContainers = @('wtctrl-dev-postgres', 'wtctrl-dev-mosquitto') |
    Where-Object { -not (Test-DockerContainer $_) }

if ($missingContainers) {
    Write-Fail "Не запущены контейнеры: $($missingContainers -join ', ')"
    Write-Host "  Запустите: docker-compose up -d postgres mosquitto" -ForegroundColor Yellow
    exit 1
}

Write-Ok "postgres и mosquitto работают"

# Docker API конкурирует с локальным за один MQTT ClientId — останавливаем его.
if (Test-DockerContainer 'wtctrl-dev-api') {
    Write-Warn "Контейнер wtctrl-dev-api запущен — останавливаем (конфликт MQTT ClientId)..."
    docker stop wtctrl-dev-api | Out-Null
    Write-Ok "wtctrl-dev-api остановлен"
}

# ── 2. Сборка ─────────────────────────────────────────────────────────────────

if (-not $SkipBuild) {
    Write-Step "Сборка решения..."
    dotnet build "$root\Wintime.Control.sln" --configuration Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet build завершился с ошибкой"
        exit 1
    }
    Write-Ok "Сборка успешна"
}

# ── 3. Тесты ──────────────────────────────────────────────────────────────────

if (-not $SkipTests) {
    Write-Step "Запуск тестов..."
    dotnet test "$root\Wintime.Control.sln" --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Тесты завершились с ошибками — запуск прерван"
        exit 1
    }
    Write-Ok "Все тесты прошли"

    Write-Step "Запуск frontend-тестов (Vitest)..."
    Push-Location "$root\Wintime-Control-Frontend"
    try {
        if (-not (Test-Path 'node_modules')) { npm install }
        npm run test
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Frontend-тесты упали"
            exit 1
        }
    } finally {
        Pop-Location
    }
    Write-Ok "Frontend-тесты прошли"
}

# ── 4. Применение миграций ────────────────────────────────────────────────────

Write-Step "Применение миграций БД..."
dotnet ef database update `
    --project "$root\Wintime.Control.Infrastructure" `
    --startup-project "$root\Wintime.Control.API"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Применение миграций завершилось с ошибкой"
    exit 1
}
Write-Ok "Миграции применены"

# ── 5. HTTPS-сертификат для доступа по сети (планшет / QR-сканер) ──────────────
# Камере браузера нужен secure context (HTTPS) — по http с планшета сканер не
# запустится. Сертификат выпускает локальный CA mkcert; корень certs\rootCA.pem
# ставится на планшет (Android: Установить сертификат ЦС; iOS: профиль + доверие).

Write-Step "Проверка HTTPS-сертификата..."
$certPath = Join-Path $root 'certs\dev.pfx'
$certPass = 'changeit'
$httpsUrl = $null

# LAN-IP для SAN и подсказки. Явный -HostIp имеет приоритет; иначе берём адреса
# интерфейсов с default gateway (так отсеиваются виртуальные адаптеры Docker/WSL,
# у которых шлюза нет). APIPA (169.254.*) исключаем.
if ($HostIp) {
    $lanIps = @($HostIp)
} else {
    $lanIps = @(Get-NetIPConfiguration |
        Where-Object { $_.IPv4DefaultGateway -and $_.NetAdapter.Status -eq 'Up' } |
        ForEach-Object { $_.IPv4Address.IPAddress } |
        Where-Object { $_ -and $_ -notlike '169.254.*' } |
        Select-Object -Unique)
}
$lanIp = $lanIps | Select-Object -First 1   # для подсказки в итогах

if (-not (Test-Path $certPath)) {
    $mkcert = Get-Command mkcert -ErrorAction SilentlyContinue
    if ($mkcert -and $lanIps) {
        Write-Warn "Сертификат не найден — генерирую через mkcert (IP: $($lanIps -join ', '))..."
        New-Item -ItemType Directory -Force (Split-Path $certPath) | Out-Null
        & $mkcert.Source -pkcs12 -p12-file $certPath @lanIps 127.0.0.1 localhost
        Copy-Item (Join-Path (& $mkcert.Source -CAROOT) 'rootCA.pem') `
            (Join-Path $root 'certs\rootCA.pem') -Force -ErrorAction SilentlyContinue
    } else {
        Write-Warn "mkcert недоступен или не определён LAN-IP — HTTPS по сети выключен."
    }
}

if (Test-Path $certPath) {
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path     = $certPath
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $certPass
    $httpsUrl = 'https://0.0.0.0:7071'
    Write-Ok "HTTPS включён (сертификат: certs\dev.pfx)"

    # Доступ с планшета требует открытого входящего порта 7071 в файрволе.
    $fwRule = Get-NetFirewallRule -DisplayName 'Wintime Control dev (HTTPS 7071)' -ErrorAction SilentlyContinue
    if (-not $fwRule) {
        Write-Warn "Нет правила файрвола на 7071. Выполните ОДИН раз в PowerShell от администратора:"
        Write-Host "     New-NetFirewallRule -DisplayName 'Wintime Control dev (HTTPS 7071)' -Direction Inbound -Protocol TCP -LocalPort 7071 -Action Allow" -ForegroundColor DarkYellow
    }
} else {
    Write-Warn "Запуск без HTTPS — камера QR-сканера с планшета работать не будет."
}

# ── 6. Запуск API ─────────────────────────────────────────────────────────────

Write-Step "Запуск API..."
# Бинд на 0.0.0.0 — чтобы приложение было доступно с других устройств в сети
# (планшет). Файрвол должен пропускать входящие TCP 5007 и 7071.
$apiUrls = if ($httpsUrl) { "$httpsUrl;http://0.0.0.0:5007" } else { "http://0.0.0.0:5007" }
$apiProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --project `"$root\Wintime.Control.API`" --no-build --urls $apiUrls" `
    -PassThru
Write-Host "  PID: $($apiProc.Id)"

Write-Step "Ожидание готовности API (до 60 сек)..."
if (-not (Wait-ForService "http://localhost:5007/swagger/v1/swagger.json" 60)) {
    Write-Fail "API не запустился за 60 сек (PID $($apiProc.Id))"
    exit 1
}
Write-Ok "API готов → http://localhost:5007"

# ── 7. Запуск Emulator ────────────────────────────────────────────────────────

Write-Step "Запуск Emulator..."
$emulatorProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --project `"$root\Wintime.Control.Emulator`" --no-build --urls http://localhost:5002" `
    -PassThru
Write-Host "  PID: $($emulatorProc.Id)"

Write-Step "Ожидание готовности Emulator (до 30 сек)..."
if (-not (Wait-ForService "http://localhost:5002/api/emulator/instances" 30)) {
    Write-Fail "Emulator не запустился за 30 сек (PID $($emulatorProc.Id))"
    exit 1
}
Write-Ok "Emulator готов → http://localhost:5002"

# ── 8. Запуск IMM-инстансов ───────────────────────────────────────────────────

Write-Step "Запуск IMM-инстансов..."

$jsonHeaders = @{ 'Content-Type' = 'application/json' }
$started = 0

try {
    $presetIds = Invoke-RestMethod "http://localhost:5002/api/presets/list" -ErrorAction Stop

    if ($presetIds -and $presetIds.Count -gt 0) {
        foreach ($immId in $presetIds) {
            try {
                $preset = Invoke-RestMethod "http://localhost:5002/api/presets/$immId" -ErrorAction Stop
                $body = $preset | ConvertTo-Json -Depth 10
                Invoke-RestMethod "http://localhost:5002/api/emulator/instances" `
                    -Method Post -Body $body -Headers $jsonHeaders -ErrorAction Stop
                Write-Host "  IMM стартован: $immId" -ForegroundColor DarkGreen
                $started++
            } catch {
                Write-Warn "Не удалось запустить IMM $immId`: $_"
            }
        }
    } else {
        Write-Host "  Пресеты не найдены, запускаем 2 тестовых IMM..." -ForegroundColor Yellow
        1..2 | ForEach-Object {
            $immId = [System.Guid]::NewGuid().ToString()
            $body = @{
                immId             = $immId
                messagesPerMinute = 10
                profile           = @(
                    @{ mode = 'manual'; durationSeconds = 60 }
                    @{ mode = 'auto';   durationSeconds = 600 }
                    @{ mode = 'idle';   durationSeconds = 60 }
                )
                sensorConfigs = @()
            } | ConvertTo-Json -Depth 5
            Invoke-RestMethod "http://localhost:5002/api/emulator/instances" `
                -Method Post -Body $body -Headers $jsonHeaders -ErrorAction Stop
            Write-Host "  IMM стартован: $immId" -ForegroundColor DarkGreen
            $started++
        }
    }
} catch {
    Write-Warn "Не удалось загрузить пресеты: $_"
}

Write-Ok "Запущено IMM-инстансов: $started"

# ── 9. Frontend dev-сервер (опционально) ──────────────────────────────────────

$frontendProc = $null
$browserUrl = "http://localhost:5007"

if ($Dev) {
    Write-Step "Запуск npm run dev..."
    $frontendProc = Start-Process -FilePath "npm" `
        -ArgumentList "run", "dev" `
        -WorkingDirectory "$root\Wintime-Control-Frontend" `
        -PassThru
    Write-Host "  PID: $($frontendProc.Id)"
    $browserUrl = "http://localhost:3000"
}

# ── 10. Открываем браузер ─────────────────────────────────────────────────────

Start-Sleep -Seconds 1
Start-Process $browserUrl

# ── 11. Итоги ────────────────────────────────────────────────────────────────

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
Write-Host "  Dev-окружение запущено" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

$summary = [System.Collections.Generic.List[PSObject]]::new()
$summary.Add([PSCustomObject]@{ Процесс = 'API';      PID = $apiProc.Id;      URL = 'http://localhost:5007' })
$summary.Add([PSCustomObject]@{ Процесс = 'Emulator'; PID = $emulatorProc.Id; URL = 'http://localhost:5002' })
if ($frontendProc) {
    $summary.Add([PSCustomObject]@{ Процесс = 'Frontend dev'; PID = $frontendProc.Id; URL = 'http://localhost:3000' })
}
$summary | Format-Table -AutoSize

Write-Host "  IMM-инстансов: $started" -ForegroundColor White
Write-Host "  Swagger:       http://localhost:5007/swagger" -ForegroundColor White
if ($httpsUrl -and $lanIp) {
    Write-Host "  С планшета:    https://${lanIp}:7071  (нужен rootCA.pem на устройстве + порт 7071 в файрволе)" -ForegroundColor White
}
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━`n" -ForegroundColor Cyan
