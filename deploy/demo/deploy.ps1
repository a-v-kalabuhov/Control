# Деплой публичного демо Control на VPS (control-demo.ru). Подробно — ASSEMBLE.md.
#   .\deploy.ps1               сборка -> push в GHCR -> пакет на VPS -> pull + up -d -> проверка
#   .\deploy.ps1 -ResetDb      то же + сброс БД свежим сидом (45–60 мин)
#   .\deploy.ps1 -ResetDbOnly  только сброс БД (перед показом)
#   .\deploy.ps1 -Certs        только сертификат (fullchain + ключ) на VPS и перезапуск API
#   -Force                     не проверять чистоту дерева и то, что VERSION поднят
param(
    [switch]$ResetDb,
    [switch]$ResetDbOnly,
    [switch]$Certs,
    [switch]$Force
)
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # кириллица из лога на VPS
$PSNativeCommandUseErrorActionPreference = $true
$ScriptDir    = $PSScriptRoot
$ControlRoot  = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$PackageDir   = Join-Path $ScriptDir "package"
$SeedFile     = Join-Path $ScriptDir "seed\seed_demo.sql"
$Registry     = "ghcr.io/a-v-kalabuhov/control"
$ApiContainer = "wtctrl-demo-api"
$MinFreeMb    = 1536

$cfgPath = Join-Path $ScriptDir "deploy.local.psd1"
if (-not (Test-Path $cfgPath)) {
    throw "Нет $cfgPath — скопируйте deploy.local.example.psd1 и заполните (ASSEMBLE.md, «Первичная настройка»)"
}
$cfg     = Import-PowerShellDataFile $cfgPath
$Target  = "$($cfg.User)@$($cfg.Host)"
$Remote  = $cfg.RemoteDir
$SshOpts = @("-o", "BatchMode=yes", "-o", "ServerAliveInterval=30")

function Invoke-Remote([string]$Command) { ssh @SshOpts $Target $Command }

function Copy-ToRemote([string[]]$Files, [string]$Dest) { scp @SshOpts @Files "${Target}:$Dest" }

function Get-RemoteImage {
    $PSNativeCommandUseErrorActionPreference = $false   # контейнера может ещё не быть
    $img = ssh @SshOpts $Target "docker inspect -f '{{.Config.Image}}' $ApiContainer 2>/dev/null"
    $PSNativeCommandUseErrorActionPreference = $true
    return "$img".Trim()
}

function Assert-FreeSpace {
    $mb = [int]("$(Invoke-Remote "df -BM --output=avail / | tail -1")" -replace '\D', '')
    if ($mb -lt $MinFreeMb) { throw "На VPS свободно $mb МБ, нужно не меньше $MinFreeMb МБ (ASSEMBLE.md, «Диск»)" }
}

function Wait-Site {
    $url = "https://$($cfg.Domain)/health"
    for ($i = 0; $i -lt 24; $i++) {
        try {
            $r = Invoke-WebRequest $url -TimeoutSec 10 -UseBasicParsing
            if ($r.StatusCode -eq 200) { Write-Host "    $url -> 200"; return }
        } catch { }
        Start-Sleep -Seconds 5
    }
    Invoke-Remote "cd $Remote && docker compose logs --tail 100 api"
    throw "$url не отвечает 200 за 2 мин"
}

# Сброс БД идёт на VPS в фоне (nohup): обрыв SSH или закрытая крышка ноутбука его не останавливают.
# Здесь только опрашиваем лог раз в 30 с.
function Invoke-ResetDb {
    $log = "$Remote/reset-db.log"
    Invoke-Remote "cd $Remote && nohup bash reset-db.sh > reset-db.log 2>&1 < /dev/null & echo started" | Out-Null
    $seen = 0
    $started = Get-Date
    while ($true) {
        Start-Sleep -Seconds 30
        if (((Get-Date) - $started).TotalMinutes -gt 90) {
            throw "Сброс БД не завершился за 90 мин — см. ~/$log"
        }
        $PSNativeCommandUseErrorActionPreference = $false   # временный сбой SSH не должен ронять опрос
        $lines = @(ssh @SshOpts $Target "cat $log 2>/dev/null")
        $sshOk = $LASTEXITCODE -eq 0
        $PSNativeCommandUseErrorActionPreference = $true
        if (-not $sshOk) { Write-Host "    (нет связи с VPS, повторяю)"; continue }
        if ($lines.Count -gt $seen) {
            $lines[$seen..($lines.Count - 1)] | ForEach-Object { Write-Host "    $_" }
            $seen = $lines.Count
        }
        if ($lines -contains "Сброс БД завершён.") { return }
        $PSNativeCommandUseErrorActionPreference = $false
        ssh @SshOpts $Target "pgrep -f '[r]eset-db.sh'" | Out-Null
        $alive = $LASTEXITCODE -eq 0
        $PSNativeCommandUseErrorActionPreference = $true
        if (-not $alive) {
            Invoke-Remote "tail -n 30 $log"
            throw "Сброс БД упал — см. ~/wtctrl-demo/reset-db.log"
        }
    }
}

function Publish-Certs {
    $local = Join-Path $PackageDir "certs"
    New-Item -ItemType Directory -Force $local | Out-Null
    # Kestrel отдаёт цепочку из Path: сертификат домена + сертификат УЦ
    $chain = (Get-Content $cfg.CertFile -Raw).TrimEnd() + "`n" + (Get-Content $cfg.CaFile -Raw).TrimEnd() + "`n"
    [IO.File]::WriteAllText((Join-Path $local "fullchain.pem"), $chain)
    Copy-Item $cfg.KeyFile (Join-Path $local "privkey.pem") -Force
    Invoke-Remote "mkdir -p $Remote/certs"
    Copy-ToRemote @((Join-Path $local "fullchain.pem"), (Join-Path $local "privkey.pem")) "$Remote/certs/"
    # API в контейнере работает от пользователя app (не deploy) — ему нужно чтение файлов
    Invoke-Remote "chmod 755 $Remote/certs && chmod 644 $Remote/certs/fullchain.pem $Remote/certs/privkey.pem"
}

function Build-And-Push([string]$Version) {
    Write-Host "==> Сборка фронта..."
    Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
    try { npm ci; npm run build } finally { Pop-Location }
    Write-Host "==> Publish и образ API $Version..."
    Push-Location $ControlRoot
    try {
        dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
        docker build -f Wintime.Control.API\Dockerfile.prod -t "$Registry/api:$Version" -t "$Registry/api:latest" .
    } finally { Pop-Location }
    Write-Host "==> Push в GHCR (нужен docker login ghcr.io)..."
    docker push "$Registry/api:$Version"
    docker push "$Registry/api:latest"
}

try { Invoke-Remote "true" }
catch { throw "Нет входа по ключу на $Target — см. ASSEMBLE.md, «Первичная настройка», шаг 1" }

if ($Certs) {
    Publish-Certs
    Invoke-Remote "cd $Remote && if [ -n `"`$(docker compose ps -q api 2>/dev/null)`" ]; then docker compose restart api; fi"
    Write-Host "Сертификат обновлён."
    return
}

if ($ResetDbOnly) {
    Copy-ToRemote @((Join-Path $PackageDir "reset-db.sh"), $SeedFile) "$Remote/"
} else {
    $Version = (Get-Content (Join-Path $ControlRoot "VERSION") -Raw).Trim()
    Write-Host "==> Версия $Version"
    if (-not $Force) {
        if (git -C $ControlRoot status --porcelain) { throw "Рабочее дерево не чистое — закоммитьте изменения (или -Force)" }
        if ((Get-RemoteImage) -eq "$Registry/api:$Version") { throw "На VPS уже $Version — поднимите VERSION (или -Force)" }
    }
    Assert-FreeSpace
    Build-And-Push $Version

    Write-Host "==> Пакет на VPS..."
    Invoke-Remote "mkdir -p $Remote"
    Copy-ToRemote @((Join-Path $PackageDir "docker-compose.yml"), (Join-Path $PackageDir "mosquitto.conf"),
                    (Join-Path $PackageDir ".env.example"), (Join-Path $PackageDir "reset-db.sh"), $SeedFile) "$Remote/"
    try { Invoke-Remote "test -f $Remote/.env && test -f $Remote/certs/fullchain.pem && test -f $Remote/certs/privkey.pem" }
    catch { throw "На VPS нет $Remote/.env или сертификата — ASSEMBLE.md, «Первичная настройка», шаги 5–6" }

    Write-Host "==> pull + up -d..."
    Invoke-Remote "cd $Remote && sed -i '/^API_TAG=/d' .env && echo API_TAG=$Version >> .env && docker compose pull api && docker compose up -d"
    Wait-Site
    $img = Get-RemoteImage
    if ($img -ne "$Registry/api:$Version") { throw "На VPS запущен $img, ожидался $Registry/api:$Version" }
    Invoke-Remote "docker image prune -f" | Out-Null
}

if ($ResetDb -or $ResetDbOnly) {
    Write-Host "==> Сброс БД (45–60 мин, в фоне на VPS, лог ~/$Remote/reset-db.log)..."
    Invoke-ResetDb
    Wait-Site
}

Write-Host "Готово: https://$($cfg.Domain)"
