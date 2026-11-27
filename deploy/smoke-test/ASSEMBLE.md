# Сборка пакета для выгрузки (автор)

1. `pwsh ./build-and-save.ps1` — образы в `dist/images/` (api, migrations, connector, emulator, postgres, mosquitto).
1a. `pwsh ./build-report-dump.ps1` — дамп БД для стенда отчётов в `package/initdb-report/`
    (схема из текущих миграций + `seed_equipment_report.sql`, ~1,5 мин). Перезапускать при изменении сида;
    при новых миграциях пересобирать дамп не обязательно — их после восстановления применит `migrations`, а не API.
2. Скопировать всё из `dist/images/` → `package/images/`.
3. Заархивировать папку `package/` (без `.env`, если хотите — с `.env.example`) → залить в облако/шару.
4. Коллега скачивает архив, распаковывает, работает по `TESTING_GUIDE.md`.

Отличие от `deploy/pzp/`: нет реального железа, нет HTTPS/mkcert (мобильный QR не участвует в этом
тестировании), добавлен образ `wintime/emulator` — виртуальный ТПА вместо реального шлюза USR-IO424.
