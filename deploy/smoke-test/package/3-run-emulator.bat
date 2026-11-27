@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo Запуск эмулятора Borche (интерактивная консоль)...
echo Для выхода: пункт меню "Выход", либо Ctrl+C.
rem --use-aliases: без него контейнер "compose run" не получает DNS-имя сервиса "emulator",
rem и коннектор (device.host = emulator) не может к нему подключиться.
docker compose -f docker-compose.test.yml run --rm -it --use-aliases emulator --config-dir /app/config
pause >nul
