@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
docker compose -f docker-compose.test-report.yml down
if errorlevel 1 (
    echo Ошибка остановки стенда отчётов. Проверьте сообщения Docker Compose выше.
    pause >nul
    exit /b 1
)
echo Стенд отчётов остановлен (данные БД в volume сохранены). Вернуть исходные тестовые данные: docker compose -f docker-compose.test-report.yml down -v, затем 2-start-report.bat
pause >nul
