#!/usr/bin/env bash
# Сброс БД демо: удаляет volume postgres и грузит свежий seed_demo.sql (даты — от момента запуска).
# Запуск из папки пакета на VPS: bash reset-db.sh   (deploy.ps1 -ResetDb / -ResetDbOnly вызывает его сам)
# На 1 ядре сид грузится 45–60 мин; всё это время демо недоступно.
set -euo pipefail
cd "$(dirname "$0")"

NEED_GB=7
DB=WintimeControlDb
PG=wtctrl-demo-postgres

[ -f seed_demo.sql ] || { echo "Нет seed_demo.sql рядом со скриптом" >&2; exit 1; }

echo "==> 1/6 Останавливаю API и удаляю БД..."
docker compose stop api
docker compose rm -f -s postgres
docker volume rm -f wtctrl-demo_postgres_data >/dev/null

avail=$(df -BG --output=avail / | tail -1 | tr -dc '0-9')
if [ "$avail" -lt "$NEED_GB" ]; then
  echo "Свободно ${avail} ГБ, нужно не меньше ${NEED_GB} ГБ. API остановлен." >&2
  echo "Освободите диск или уменьшите ppc/days в начале seed_demo.sql и запустите снова." >&2
  exit 1
fi

echo "==> 2/6 Поднимаю postgres..."
docker compose up -d postgres
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$PG")" = "healthy" ]; do sleep 2; done

echo "==> 3/6 Запускаю API: миграции и демо-аккаунты..."
docker compose up -d api
for i in $(seq 1 90); do
  if docker exec "$PG" psql -U postgres -d "$DB" -Atc "SELECT 1 FROM \"Users\" WHERE \"UserName\" = 'adjuster'" 2>/dev/null | grep -q 1; then
    break
  fi
  if [ "$i" -eq 90 ]; then
    echo "API не создал демо-аккаунты за 3 мин:" >&2
    docker compose logs --tail 50 api >&2
    exit 1
  fi
  sleep 2
done

echo "==> 4/6 Останавливаю API на время загрузки (фоновые воркеры не должны видеть полусид)..."
docker compose stop api

echo "==> 5/6 Загружаю сид (на 1 ядре — 45–60 мин)..."
start=$(date +%s)
docker exec -i "$PG" psql -U postgres -d "$DB" -v ON_ERROR_STOP=1 -q < seed_demo.sql
size=$(docker exec "$PG" psql -U postgres -d "$DB" -Atc "SELECT pg_size_pretty(pg_database_size(current_database()))")
echo "    готово за $(( ($(date +%s) - start) / 60 )) мин, размер БД ${size}"

echo "==> 6/6 Запускаю API..."
docker compose up -d api
echo "Сброс БД завершён."
