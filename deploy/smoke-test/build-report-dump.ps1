# Готовит дамп БД для стенда отчётов: package\initdb-report\10-report-data.sql.gz
# Временный postgres -> схема из миграций текущих исходников -> seed_equipment_report.sql -> pg_dump.
# Перезапускать при изменении сида или миграций (и тогда же пересобирать образ API, build-and-save.ps1).
# У тестировщика дамп разворачивается сам при первом старте 2-start-report.bat (пустой volume).
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$OutDir      = Join-Path $ScriptDir "package\initdb-report"
$Container   = "wtctrl-report-dump-build"
$Db          = "WintimeControl_ReportTest"
$Tmp         = Join-Path ([IO.Path]::GetTempPath()) "wtctrl-report-dump"

New-Item -ItemType Directory -Force $Tmp, $OutDir | Out-Null

Write-Host "==> 1/5 SQL-скрипт миграций..."
Push-Location $ControlRoot
dotnet ef migrations script --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API `
    -o (Join-Path $Tmp "schema.sql")
Pop-Location

Write-Host "==> 2/5 Временный postgres..."
docker rm -f $Container 2>$null | Out-Null
docker run -d --name $Container -e POSTGRES_PASSWORD=build -e POSTGRES_DB=$Db postgres:16-alpine | Out-Null
try {
    # -h 127.0.0.1: на время init-скриптов образ слушает только unix-сокет, по TCP — уже рабочий сервер
    $ready = $false
    $PSNativeCommandUseErrorActionPreference = $false   # ненулевой код pg_isready здесь — норма
    for ($i = 0; $i -lt 60; $i++) {
        docker exec $Container pg_isready -h 127.0.0.1 -U postgres -d $Db 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    $PSNativeCommandUseErrorActionPreference = $true
    if (-not $ready) { throw "postgres не поднялся за 60 с" }

    docker cp (Join-Path $Tmp "schema.sql") "${Container}:/tmp/schema.sql"
    docker cp (Join-Path $ControlRoot "seed_equipment_report.sql") "${Container}:/tmp/seed.sql"

    Write-Host "==> 3/5 Схема..."
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/schema.sql | Out-Null

    Write-Host "==> 4/5 Тестовые данные (генерация займёт пару минут)..."
    docker exec $Container psql -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/seed.sql

    Write-Host "==> 5/5 pg_dump..."
    docker exec $Container pg_dump -U postgres -d $Db --no-owner --no-privileges -Z 6 -f /tmp/dump.sql.gz
    docker cp "${Container}:/tmp/dump.sql.gz" (Join-Path $OutDir "10-report-data.sql.gz")
}
finally {
    docker rm -f $Container | Out-Null
}

# После восстановления у таблиц нет статистики — без неё первые запросы отчёта медленные
Set-Content -Path (Join-Path $OutDir "20-analyze.sql") -Value "ANALYZE;" -Encoding ascii

$size = [math]::Round((Get-Item (Join-Path $OutDir "10-report-data.sql.gz")).Length / 1MB, 1)
Write-Host "Готово: package\initdb-report\10-report-data.sql.gz ($size МБ)."
