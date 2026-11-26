@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
if not exist ".env" ( echo Нет файла .env — скопируйте .env.example в .env и заполните. & pause & exit /b 1 )
rem emulator сюда намеренно не входит - это интерактивный TUI, запускается отдельно через 3-run-emulator.bat
if not exist "initdb-report\10-report-data.sql.gz" ( echo Нет файла initdb-report\10-report-data.sql.gz - пакет распакован полностью? & pause & exit /b 1 )
echo При первом запуске в БД загружаются тестовые данные - это займёт несколько минут.
echo Вход: admin / Demo@123 (наладчики adj1, adj2 - тот же пароль).
docker compose -f docker-compose.test-report.yml stop api
if errorlevel 1 ( echo ОШИБКА остановки API & pause & exit /b 1 )
docker compose -f docker-compose.test-report.yml up -d --force-recreate migrations api connector
if errorlevel 1 (
  echo ОШИБКА миграций или запуска. API оставлен остановленным.
  docker compose -f docker-compose.test-report.yml logs --tail 100 migrations api
  pause & exit /b 1
)
echo Стек запущен. Дашборд: http://localhost:5000
pause >nul
