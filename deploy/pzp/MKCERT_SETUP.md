# Разовая подготовка mkcert (автор, у себя)

1. Установить mkcert (Windows): `choco install mkcert` или скачать `mkcert.exe` c
   github.com/FiloSottile/mkcert/releases.
2. Создать локальный CA: `mkcert -install` (создаёт CAROOT с rootCA.pem + rootCA-key.pem).
3. Найти CAROOT: `mkcert -CAROOT`.
4. Положить в пакет:
   - `mkcert.exe` → `package/mkcert.exe`
   - весь каталог CAROOT → `package/CAROOT/` (rootCA.pem + rootCA-key.pem)
   - публичный `rootCA.pem` → `package/certs/rootCA.pem` (для установки на планшеты)
5. `certs/pzp.pfx` НЕ кладём заранее — его генерит `4-make-cert.bat` на площадке по факт. IP.

PFX-пароль у mkcert фиксированный — `changeit`; он же в `.env` как CERT_PASSWORD.
