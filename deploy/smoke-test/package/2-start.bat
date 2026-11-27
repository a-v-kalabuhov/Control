@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
if not exist ".env" ( echo Нет файла .env — скопируйте .env.example в .env и заполните. & pause & exit /b 1 )
rem emulator сюда намеренно не входит - это интерактивный TUI, запускается отдельно через 3-run-emulator.bat
docker compose -f docker-compose.test.yml stop api
if errorlevel 1 ( echo ОШИБКА остановки API & pause & exit /b 1 )
docker compose -f docker-compose.test.yml up -d --force-recreate migrations api connector
if errorlevel 1 (
  echo ОШИБКА миграций или запуска. API оставлен остановленным.
  docker compose -f docker-compose.test.yml logs --tail 100 migrations api
  pause & exit /b 1
)
echo Стек запущен. Дашборд: http://localhost:5000
pause >nul
