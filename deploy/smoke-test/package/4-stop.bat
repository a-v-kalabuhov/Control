@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
docker compose -f docker-compose.test.yml down
echo Стек остановлен (данные БД в volume сохранены). Для полной очистки: docker compose -f docker-compose.test.yml down -v
pause >nul
