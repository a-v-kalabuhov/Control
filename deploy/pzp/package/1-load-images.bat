@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo Загрузка Docker-образов из images\ ...
for %%f in (images\*.tar) do (
  echo   %%f
  docker load -i "%%f"
  if errorlevel 1 ( echo ОШИБКА загрузки %%f & pause & exit /b 1 )
)
echo Готово. Нажмите любую клавишу.
pause >nul
