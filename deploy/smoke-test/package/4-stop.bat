@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
docker compose -f docker-compose.test.yml down
if errorlevel 1 (
    echo Ошибка остановки стека. Проверьте сообщения Docker Compose выше.
    pause >nul
    exit /b 1
)
echo Стек остановлен (данные БД в volume сохранены). Для полной очистки: docker compose -f docker-compose.test.yml down -v
pause >nul
