# ADR-0033. Отдельный one-shot application/container для EF Core-миграций

- Status: Accepted
- Date: 2026-11-26
- Related: TD-06

## Контекст

`Wintime.Control.API` при старте синхронно вызывал `Database.Migrate()` вместе с прикладными
сидами. Изменение схемы было побочным эффектом запуска web-приложения, а при нескольких
экземплярах API возникала бы гонка за DDL. Для текущих одиночных Compose-установок нужен
явный порядок: PostgreSQL готов → миграции успешны → API запущен. При штатном обновлении
старый API останавливается до миграций, поскольку частично обновлённая схема может быть
несовместима со старым кодом.

## Решение

1. `Wintime.Control.Migrations` — console application .NET 9, ссылающееся на Infrastructure
   и использующее существующий `ControlDbContext`. Тестируемый runner принимает `IConfiguration`,
   вызывает `Database.MigrateAsync()` и возвращает `0` при успехе/no-op, `1` при ошибке.
2. Тот же runner используется локально, в integration test factory и в production/dev Docker-
   образах. Migrator не содержит Identity, MQTT, hosted services, controllers и доменных сидов.
3. API больше не применяет миграции ни в одной среде. Демо-пользователи, первый production
   administrator и смена по умолчанию остаются его прикладной инициализацией.
4. Во всех Compose-конфигурациях `migrations` зависит от healthy PostgreSQL и имеет
   `restart: "no"`; API зависит от `service_completed_successfully`. Exited-контейнер остаётся
   доступен для `ps -a` и `logs` до явного пересоздания.
5. Deploy останавливает старый API, затем выполняет `up -d --force-recreate migrations api`.
   API и migrations выпускаются одной версией. При сбое показываются логи, старый API не
   возобновляется и автоматический rollback не выполняется.
6. `dotnet ef migrations add` остаётся способом создания миграций. `dotnet ef database update`
   больше не является документированным штатным способом их применения.

## Альтернативы

- **`--migrate-only` в API.** Отклонено: сохраняет web-зависимости, лишнюю инициализацию и
  размытую границу ответственности ради одного образа.
- **`dotnet ef database update` в SDK-контейнере.** Отклонено: требует SDK/EF tooling в
  deployment и дополнительных project/startup-project параметров.
- **Автомиграции только в Development.** Отклонено: создаёт два механизма и риск случайного
  включения вне разработки.
- **Оставить старый API работающим при сбое.** Отклонено для одиночной установки: корректность
  потребовала бы backward-compatible expand/contract-процесса и zero-downtime deployment.

## Последствия

- Запуск и сбой схемы видны отдельно от жизненного цикла API; один и тот же код проверяется
  integration-тестами и работает в контейнере.
- Штатное обновление имеет downtime. Сбой миграции оставляет систему недоступной до исправления;
  backup и восстановление остаются отдельной эксплуатационной процедурой.
- `docker restart api` и restart policy не применяют миграции. Повторный запуск уже успешного
  one-shot service при обновлении обеспечивается явным `--force-recreate`.
- Для будущего multi-instance/zero-downtime deployment потребуется отдельное решение о блокировках
  миграций и совместимости схемы; TD-06 его не реализует.
