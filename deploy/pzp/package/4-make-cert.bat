@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "IP=%~1"
if "%IP%"=="" (
  echo Usage: 4-make-cert.bat ^<LAN-IP^>   ^(напр. 4-make-cert.bat 192.168.1.50^)
  pause & exit /b 1
)
set "CAROOT=%~dp0CAROOT"
if not exist "certs" mkdir certs
echo Генерация сертификата для %IP% ...
"%~dp0mkcert.exe" -pkcs12 -p12-file certs\pzp.pfx %IP% 127.0.0.1 localhost
if errorlevel 1 ( echo ОШИБКА mkcert & pause & exit /b 1 )
echo.
echo Готово: certs\pzp.pfx (пароль PFX = changeit; он же CERT_PASSWORD в .env).
echo Корень для планшетов: certs\rootCA.pem
pause >nul
