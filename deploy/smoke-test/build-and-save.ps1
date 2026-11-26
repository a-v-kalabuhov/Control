param(
    # Явный путь к репозиторию коннектора относительно этого скрипта (deploy/smoke-test)
    [string]$ConnectorRepo
)

function Resolve-ConnectorRepository {
    param(
        [string]$ControlRoot,
        [string]$ScriptDir,
        [string]$Override
    )

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        $overridePath = [IO.Path]::IsPathRooted($Override) ? $Override : (Join-Path $ScriptDir $Override)
        return (Resolve-Path $overridePath).Path
    }

    # Обычный checkout: Sources/Control -> Sources/Connectors. В native worktree
    # поднимаемся по предкам ControlRoot до того же каталога Sources.
    for ($ancestor = Get-Item $ControlRoot; $null -ne $ancestor.Parent; $ancestor = $ancestor.Parent) {
        $candidate = Join-Path $ancestor.Parent.FullName "Connectors\Wintime.Connector.UsrModbus"
        if (Test-Path (Join-Path $candidate "Dockerfile") -PathType Leaf) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Не найден репозиторий Wintime.Connector.UsrModbus. Укажите его через -ConnectorRepo."
}

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true   # non-zero exit from native tools -> terminating error
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path   # корень репо Control
$ConnRoot    = Resolve-ConnectorRepository -ControlRoot $ControlRoot -ScriptDir $ScriptDir -Override $ConnectorRepo

Write-Host "==> 1/7 Сборка фронта API..."
Push-Location (Join-Path $ControlRoot "Wintime-Control-Frontend")
npm ci
npm run build
Pop-Location

Write-Host "==> 2/7 Publish + образ API..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.API -c Release -o .\publish\api /p:UseAppHost=false /p:SkipFrontendBuild=true
docker build -f Wintime.Control.API\Dockerfile.prod -t wintime/api:smoke-test .
Pop-Location

Write-Host "==> 3/7 Publish + образ migrations..."
Push-Location $ControlRoot
dotnet publish Wintime.Control.Migrations -c Release -o .\publish\migrations /p:UseAppHost=false
docker build -f Wintime.Control.Migrations\Dockerfile.prod -t wintime/migrations:smoke-test .
Pop-Location

Write-Host "==> 4/7 Образ коннектора..."
Push-Location $ConnRoot
docker build -f Dockerfile -t wintime/connector:smoke-test .
Pop-Location

Write-Host "==> 5/7 Образ эмулятора Borche..."
Push-Location $ConnRoot
docker build -f Dockerfile.emulator -t wintime/emulator:smoke-test .
Pop-Location

Write-Host "==> 6/7 Базовые образы..."
docker pull postgres:16-alpine
docker pull eclipse-mosquitto:2.0

Write-Host "==> 7/7 docker save -> dist\images ..."
$imgDir = Join-Path $ScriptDir "dist\images"
New-Item -ItemType Directory -Force $imgDir | Out-Null
docker save wintime/api:smoke-test        -o (Join-Path $imgDir "api.tar")
docker save wintime/migrations:smoke-test  -o (Join-Path $imgDir "migrations.tar")
docker save wintime/connector:smoke-test  -o (Join-Path $imgDir "connector.tar")
docker save wintime/emulator:smoke-test   -o (Join-Path $imgDir "emulator.tar")
docker save postgres:16-alpine            -o (Join-Path $imgDir "postgres.tar")
docker save eclipse-mosquitto:2.0         -o (Join-Path $imgDir "mosquitto.tar")

Write-Host "Готово. Тарболы в $imgDir. Дальше: скопировать их в package\images\ и заархивировать package\ (см. ASSEMBLE.md)."
