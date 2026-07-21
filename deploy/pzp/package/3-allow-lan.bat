@echo off
chcp 65001 >nul
:: Требует прав администратора. ПКМ -> Запуск от имени администратора.
net session >nul 2>&1
if %errorlevel% neq 0 ( echo Запустите этот файл от имени Администратора. & pause & exit /b 1 )
netsh advfirewall firewall delete rule name="Wintime Control HTTP 5000" >nul 2>&1
netsh advfirewall firewall add rule name="Wintime Control HTTP 5000" dir=in action=allow protocol=TCP localport=5000
netsh advfirewall firewall delete rule name="Wintime Control HTTPS 443" >nul 2>&1
netsh advfirewall firewall add rule name="Wintime Control HTTPS 443" dir=in action=allow protocol=TCP localport=443
echo Правила брандмауэра добавлены (TCP 5000 и 443). Нажмите любую клавишу.
pause >nul
