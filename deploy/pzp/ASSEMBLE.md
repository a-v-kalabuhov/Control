# Сборка пакета для выгрузки (автор)

1. `pwsh ./build-and-save.ps1` — образы в `dist/images/`.
2. Один раз подготовить mkcert (см. MKCERT_SETUP.md) — `mkcert.exe`, `CAROOT/`,
   `certs/rootCA.pem` в `package/`.
3. Собрать выгружаемую папку: скопировать всё из `package/` + `dist/images/` → в `package/images/`.
4. Заархивировать `package/` (без `.env`, если хотите — с `.env.example`) → залить в облако.
5. Помощник качает архив, распаковывает, работает по CONFIG_GUIDE.md.
