@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo Загрузка Docker-образов из images\ ...
if not exist "images\*.tar" (
  echo ОШИБКА: в папке images\ нет .tar-файлов образов. Пакет распакован полностью?
  pause & exit /b 1
)
for %%f in (images\*.tar) do (
  echo   %%f
  docker load -i "%%f"
  if errorlevel 1 ( echo ОШИБКА загрузки %%f & pause & exit /b 1 )
)
echo Готово. Нажмите любую клавишу.
pause >nul
