# Демо-VPS: деплой (автор)

Публичное демо Control — `https://controldemo.ru`, VPS `deploy@195.208.2.44` (1 ГБ ОЗУ, 1 ядро, 8,6 ГБ).
`DemoMode=true` намеренно: открытые демо-аккаунты всех ролей. Живых данных нет (все ТПА «Нет связи»),
история — из `seed/seed_demo.sql`, заканчивается моментом последнего сброса БД.

Всё запускается с вашей машины: `.\deploy\demo\deploy.ps1` (PowerShell 7, Docker, OpenSSH, Node, .NET SDK).

## Первичная настройка (один раз)

1. **Вход по ключу для `deploy`** (под root на VPS):
   ```bash
   mkdir -p /home/deploy/.ssh
   # ключ — одной строкой, без комментария: вставка через `cat >>` легко рвёт его на части
   echo 'ssh-rsa AAAA…ваш ключ из ~/.ssh/id_rsa.pub…' > /home/deploy/.ssh/authorized_keys
   chown -R deploy:deploy /home/deploy/.ssh
   chmod go-w /home/deploy && chmod 700 /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys
   usermod -aG docker deploy                      # docker без sudo
   ```
   Проверка с вашей машины — из **PowerShell**: `ssh deploy@195.208.2.44 docker ps`. Ssh из Git Bash не
   читает ключ, если в пути профиля есть кириллица, и просит пароль; `deploy.ps1` тоже запускать из PowerShell.
2. **Снести старое демо** (под root). Старый стек с эмулятором забил диск телеметрией:
   ```bash
   docker inspect wtctrl-prod-api --format '{{ index .Config.Labels "com.docker.compose.project.working_dir" }}'
   cd <папка из вывода> && docker compose -f docker-compose.prod.yml down -v
   docker image prune -a -f
   ```
   **Данные Docker — на отдельном диске.** Системный раздел (8,6 ГБ, из них ~3,8 ГБ — Ubuntu) БД ~4,6 ГБ
   не вмещает, поэтому к VPS подключён сетевой диск 10 ГБ (`/dev/sdb`), и весь `data-root` Docker
   перенесён на него (под root, один раз):
   ```bash
   mkfs.ext4 -L dockerdata /dev/sdb        # только новый пустой диск
   mkdir -p /mnt/docker
   echo 'LABEL=dockerdata /mnt/docker ext4 defaults,nofail,_netdev 0 2' >> /etc/fstab
   mount /mnt/docker
   systemctl stop docker docker.socket
   rsync -a /var/lib/docker/ /mnt/docker/
   echo '{ "data-root": "/mnt/docker" }' > /etc/docker/daemon.json
   mkdir -p /etc/systemd/system/docker.service.d
   printf '[Unit]\nRequiresMountsFor=/mnt/docker\n' > /etc/systemd/system/docker.service.d/mount.conf
   systemctl daemon-reload && systemctl start docker
   docker info -f '{{.DockerRootDir}}'     # /mnt/docker
   df -h /mnt/docker                       # нужно ≥ 7 ГБ свободно
   ```
3. **Файрвол:** `ufw status`; если активен — `ufw allow 80/tcp && ufw allow 443/tcp`.
4. **GHCR:** проверить под `deploy`: `docker pull ghcr.io/a-v-kalabuhov/control/api:latest`. Если «denied» —
   `docker login ghcr.io -u a-v-kalabuhov` с PAT (`read:packages`). На своей машине — `docker login ghcr.io`
   с PAT (`write:packages`).
5. **Локальный конфиг:** скопировать `deploy.local.example.psd1` → `deploy.local.psd1`, указать пути к
   `.crt` домена, `.crt` УЦ и `.key`. Затем `.\deploy\demo\deploy.ps1 -Certs`.
6. **Секреты на VPS:** `.env` нужно создать **до первого деплоя** — без него `deploy.ps1` откажется работать.
   С вашей машины: `ssh deploy@195.208.2.44 mkdir -p wtctrl-demo`, затем
   `scp deploy/demo/package/.env.example deploy@195.208.2.44:wtctrl-demo/.env` и на VPS отредактировать
   `~/wtctrl-demo/.env` (`DB_PASSWORD` — длинный случайный).
7. **Первый деплой:** `.\deploy\demo\deploy.ps1 -ResetDb`.

## Обновление версии

1. Поднять `VERSION` (SemVer), закоммитить.
2. `.\deploy\demo\deploy.ps1` — сборка, push `api:<VERSION>` и `latest`, копирование пакета, `pull` + `up -d`,
   ожидание `https://controldemo.ru/health`, сверка образа, `docker image prune -af` (удаляет все образы, которые
   не использует ни один контейнер, — старые версии API не копятся).

## Сброс БД перед показом

`.\deploy\demo\deploy.ps1 -ResetDbOnly` — 45–60 мин, всё это время демо недоступно. Даты сдвигаются к
«сейчас», у `adjuster` снова 7 выданных заданий.

Сброс выполняется **в фоне на VPS** (`nohup bash reset-db.sh`), лог — `~/wtctrl-demo/reset-db.log`.
`deploy.ps1` только опрашивает лог раз в 30 с, поэтому обрыв SSH или закрытый ноутбук сброс не останавливают.
Второй сброс параллельно не запустится (`pgrep` в `deploy.ps1` и `flock` в `reset-db.sh`): повторный
`-ResetDbOnly`, пока сброс идёт, просто снова подключается к его логу, а обычный деплой отказывается работать
до окончания сброса. ssh/scp при сетевой ошибке (VPS иногда не принимает подключения подряд) повторяются до 4 раз. Если скрипт прервали, посмотреть результат: `ssh deploy@195.208.2.44 tail -n 30 ~/wtctrl-demo/reset-db.log`.

## Сертификат

Продление: заменить файлы по путям из `deploy.local.psd1` и выполнить `.\deploy\demo\deploy.ps1 -Certs`.
На VPS — `~/wtctrl-demo/certs/fullchain.pem` (домен + УЦ) и `privkey.pem` (права 644: API в контейнере
читает их не от `deploy`). Ключ с паролем не поддерживается — снять пароль: `openssl rsa -in key -out key.nopass`.

## Откат

На VPS: `cd ~/wtctrl-demo && sed -i '/^API_TAG=/d' .env && printf '\nAPI_TAG=%s\n' <старая версия> >> .env && docker compose pull api && docker compose up -d`.
Старый образ после деплоя удалён (`image prune -af`), поэтому `pull` обязателен — он заново скачает его из GHCR.
Схема БД при этом не откатывается — если в новой версии были миграции, после отката нужен `-ResetDbOnly`
со старым сидом.

## Диск

Данные Docker (образы, БД, логи контейнеров) — на сетевом диске 10 ГБ `/mnt/docker` (`data-root`, см.
«Первичная настройка», шаг 2); системный раздел 8,6 ГБ им не занят. Обе проверки ниже смотрят на диск
`docker info -f '{{.DockerRootDir}}'`. БД после сида ~4,6 ГБ и после деплоя почти не растёт. Старые образы API
чистятся при каждом деплое (`docker image prune -af` — только образы без контейнеров). `deploy.ps1` требует ≥ 1,5 ГБ свободно
перед `pull`, `reset-db.sh` — ≥ 7 ГБ перед сидом. Не хватает — уменьшить `ppc` (точек телеметрии на цикл)
или `days` в начале `seed/seed_demo.sql`.

## Неполадки

- `не отвечает 200`, но сайт открывается в браузере — сертификат УЦ не доверенный на вашей машине
  (`Invoke-WebRequest` проверяет цепочку); см. текст «Последняя ошибка» в выводе.
- `Нет входа по ключу` — шаг 1 первичной настройки.
- `docker compose logs --tail 100 api` на VPS — ошибки старта API (сертификат, БД).
- `Cannot ... certificate` в логах API — проверить, что `fullchain.pem` начинается с сертификата домена и
  права 644.
- Сид упал — `reset-db.sh` печатает ошибку psql; API остаётся на пустой БД с демо-аккаунтами. Исправить сид,
  проверить локально `pwsh deploy\demo\test-seed.ps1`, повторить `-ResetDbOnly`.
