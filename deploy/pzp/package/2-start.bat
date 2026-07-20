@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
if not exist ".env" ( echo Нет файла .env — скопируйте .env.example в .env и заполните. & pause & exit /b 1 )
if not exist "certs\pzp.pfx" ( echo Нет certs\pzp.pfx — сначала выполните 4-make-cert.bat. & pause & exit /b 1 )
docker compose -f docker-compose.prod.yml up -d
if errorlevel 1 ( echo ОШИБКА запуска & pause & exit /b 1 )
echo Стек запущен. Дашборд: http://localhost:5000
pause >nul
