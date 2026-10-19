#Requires -Version 7.3
# Локальная проверка демо-сида: временный postgres -> схема из миграций текущих исходников ->
# пользователь adjuster (как его создаёт API в DemoMode) -> seed\seed_demo.sql -> seed\check_seed.sql.
# Каждая проверка должна дать 0 нарушений. Контейнер удаляется в любом случае.
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$ScriptDir   = $PSScriptRoot
$ControlRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$SeedDir     = Join-Path $ScriptDir "seed"
$Container   = "wtctrl-demo-seed-test"
$Db          = "WintimeControlDb"
$Tmp         = Join-Path ([IO.Path]::GetTempPath()) "wtctrl-demo-seed-test"

New-Item -ItemType Directory -Force $Tmp | Out-Null

Write-Host "==> 1/5 SQL-скрипт миграций..."
Push-Location $ControlRoot
try {
    dotnet ef migrations script --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API `
        -o (Join-Path $Tmp "schema.sql")
}
finally {
    Pop-Location
}

# Демо-аккаунт adjuster: в проде его создаёт API при DemoMode=true, сид ищет его по UserName
$adjusterSql = @'
INSERT INTO "Users" ("Id","FullName","Role","IsActive","UserName","NormalizedUserName","EmailConfirmed",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
VALUES ('test-demo-adjuster','Наладчик Тестовый',2,true,'adjuster','ADJUSTER',false,false,false,true,0);
'@
[IO.File]::WriteAllText((Join-Path $Tmp "adjuster.sql"), $adjusterSql)

Write-Host "==> 2/5 Временный postgres..."
$PSNativeCommandUseErrorActionPreference = $false   # контейнера может не быть — это норма
docker rm -f $Container 2>$null | Out-Null
$PSNativeCommandUseErrorActionPreference = $true
docker run -d --name $Container -e POSTGRES_PASSWORD=test -e POSTGRES_DB=$Db postgres:16-alpine `
    postgres -c shared_buffers=128MB | Out-Null
try {
    # -h 127.0.0.1: на время init-скриптов образ слушает только unix-сокет, по TCP — уже рабочий сервер
    $ready = $false
    $PSNativeCommandUseErrorActionPreference = $false
    for ($i = 0; $i -lt 60; $i++) {
        docker exec $Container pg_isready -h 127.0.0.1 -U postgres -d $Db 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    $PSNativeCommandUseErrorActionPreference = $true
    if (-not $ready) { throw "postgres не поднялся за 60 с" }

    docker cp (Join-Path $Tmp "schema.sql")         "${Container}:/tmp/schema.sql"
    docker cp (Join-Path $Tmp "adjuster.sql")       "${Container}:/tmp/adjuster.sql"
    docker cp (Join-Path $SeedDir "seed_demo.sql")  "${Container}:/tmp/seed.sql"
    docker cp (Join-Path $SeedDir "check_seed.sql") "${Container}:/tmp/check.sql"

    Write-Host "==> 3/5 Схема и демо-аккаунт adjuster..."
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/schema.sql | Out-Null
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/adjuster.sql | Out-Null

    Write-Host "==> 4/5 Сид..."
    $sw = [Diagnostics.Stopwatch]::StartNew()
    docker exec $Container psql -q -U postgres -d $Db -v ON_ERROR_STOP=1 -f /tmp/seed.sql
    $size = docker exec $Container psql -U postgres -d $Db -At -c "SELECT pg_size_pretty(pg_database_size(current_database()))"
    Write-Host ("    сид за {0:mm\:ss}, размер БД {1}" -f $sw.Elapsed, $size)

    Write-Host "==> 5/5 Проверки..."
    $rows = docker exec $Container psql -U postgres -d $Db -At -F "|" -v ON_ERROR_STOP=1 -f /tmp/check.sql
    # Число SELECT-строк в check_seed.sql: пустой или обрезанный вывод не должен пройти как «всё в порядке»
    $ExpectedChecks = 23
    $failed = @()
    $parsed = 0
    foreach ($row in $rows) {
        if ($row -notmatch '^\w+\|\d+$') { throw "Неожиданная строка проверки: $row" }
        $name, $count = $row -split '\|'
        Write-Host ("    {0,-40} {1}" -f $name, $count)
        if ([long]$count -ne 0) { $failed += $name }
        $parsed++
    }
    if ($parsed -ne $ExpectedChecks) { throw "Получено проверок: $parsed, ожидалось: $ExpectedChecks" }
    if ($failed.Count -gt 0) { throw "Нарушены проверки: $($failed -join ', ')" }
    Write-Host "Сид в порядке."
}
finally {
    # Ошибка удаления контейнера не должна маскировать исходную ошибку проверки
    $PSNativeCommandUseErrorActionPreference = $false
    docker rm -f $Container | Out-Null
}
