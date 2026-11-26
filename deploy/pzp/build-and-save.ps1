param(
    # Путь к репозиторию коннектора относительно этого скрипта (deploy/pzp)
    [string]$ConnectorRepo = "..\..\..\Connectors\Wintime.Connector.UsrModbus"
)
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true   # non-zero exit from native tools -> terminating error
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path   # корень репо Control
$ConnRoot    = (Resolve-Path (Join-Path $ScriptDir $ConnectorRepo)).Path

$Version     = (Get-Content (Join-Path $ControlRoot "VERSION") -Raw).Trim()
Write-Host "==> Version: $Version"

Write-Host "==> 1/6 Сборка фронта API..."
Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
npm ci
npm run build
Pop-Location

Write-Host "==> 2/6 Publish + образ API..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
docker build -f Wintime.Control.API\Dockerfile.prod -t wintime/api:pilot .
Pop-Location

Write-Host "==> 3/6 Publish + образ migrations..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
docker build -f Wintime.Control.Migrations\Dockerfile.prod -t wintime/migrations:pilot .
Pop-Location

Write-Host "==> 4/6 Образ коннектора..."
Push-Location $ConnRoot
docker build -f Dockerfile -t wintime/connector:pilot .
Pop-Location

Write-Host "==> 5/6 Базовые образы..."
docker pull postgres:16-alpine
docker pull eclipse-mosquitto:2.0

Write-Host "==> 6/6 docker save -> dist\images ..."
$imgDir = Join-Path $ScriptDir "dist\images"
New-Item -ItemType Directory -Force $imgDir | Out-Null
docker save wintime/api:pilot        -o (Join-Path $imgDir "api.tar")
docker save wintime/migrations:pilot   -o (Join-Path $imgDir "migrations.tar")
docker save wintime/connector:pilot  -o (Join-Path $imgDir "connector.tar")
docker save postgres:16-alpine       -o (Join-Path $imgDir "postgres.tar")
docker save eclipse-mosquitto:2.0    -o (Join-Path $imgDir "mosquitto.tar")

Write-Host "Готово. Тарболы в $imgDir"
