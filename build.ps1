param(
    # По умолчанию — номер из файла VERSION в корне репо
    [string]$Tag = (Get-Content (Join-Path $PSScriptRoot "VERSION") -Raw).Trim(),
    [string]$Registry = "ghcr.io/a-v-kalabuhov/control"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Version: $Tag"

# 1. API frontend
Write-Host "==> Building API frontend..."
Push-Location Wintime-Control-Frontend
npm ci
npm run build
Pop-Location

# 2. Emulator frontend
Write-Host "==> Building Emulator frontend..."
Push-Location Wintime.Control.Emulator/Web
npm ci
npm run build
Pop-Location

# 3. dotnet publish
Write-Host "==> Publishing API..."
dotnet publish Wintime.Control.API -c Release -o ./publish/api /p:UseAppHost=false /p:SkipFrontendBuild=true

Write-Host "==> Publishing Emulator..."
dotnet publish Wintime.Control.Emulator -c Release -o ./publish/emulator /p:UseAppHost=false /p:SkipFrontendBuild=true

# 4. Docker build (версия + latest)
Write-Host "==> Building Docker images..."
docker build -f Wintime.Control.API/Dockerfile.prod -t "$Registry/api:$Tag" -t "$Registry/api:latest" .
docker build -f Wintime.Control.Emulator/Dockerfile.prod -t "$Registry/emulator:$Tag" -t "$Registry/emulator:latest" .

# 5. Push
Write-Host "==> Pushing to GHCR..."
docker push "$Registry/api:$Tag"
docker push "$Registry/api:latest"
docker push "$Registry/emulator:$Tag"
docker push "$Registry/emulator:latest"

Write-Host ""
Write-Host "Done. On VPS:"
Write-Host "  docker compose -f docker-compose.prod.yml pull"
Write-Host "  docker compose -f docker-compose.prod.yml up -d"
