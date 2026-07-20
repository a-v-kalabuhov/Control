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
