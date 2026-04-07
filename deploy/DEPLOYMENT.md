# GitHub Deploy для `vimovpn.icu`

Этот репозиторий теперь рассчитан на деплой через `self-hosted` GitHub runner без привязки к конкретным label.

## Как это работает

- Workflow запускается на любом доступном `self-hosted` runner этого репозитория.
- Runner делает `actions/checkout` локально на сервере.
- Затем выполняет `docker compose -f docker-compose.prod.yml up -d --build`.
- Сервис поднимается через контейнеры `app` и `caddy`.
- TLS для `vimovpn.icu` получает и обновляет сам `Caddy`, без отдельного bash-деплой-скрипта.

## Что нужно на сервере один раз

1. Установить Docker и Docker Compose plugin.
2. Установить GitHub self-hosted runner именно для этого репозитория.
3. Добавить пользователя runner в группу `docker`.
4. Разрешить этому пользователю `sudo mkdir/chown` для каталога `/srv/vimovpn` либо подготовить каталоги заранее.
5. Убедиться, что `vimovpn.icu` уже смотрит на `2.26.108.92`, а порты `80` и `443` открыты.

Пошаговая памятка для runner: `deploy/RUNNER_SETUP.md`.

## Нужные GitHub Secrets

Добавьте в `Settings -> Secrets and variables -> Actions`:

- `LETSENCRYPT_EMAIL`
- `SHOPBOT_SECRET_KEY`

## Прод-каталоги на сервере

- Данные приложения: `/srv/vimovpn/data`
- Бэкапы: `/srv/vimovpn/data/backups`
- Caddy data: `/srv/vimovpn/caddy/data`
- Caddy config: `/srv/vimovpn/caddy/config`

## Что хранится вне checkout

Это важно, потому что `actions/checkout` чистит рабочую директорию runner'а:

- `users.db` живет в `/srv/vimovpn/data/users.db`
- папка `backups` живет в `/srv/vimovpn/data/backups`
- TLS-данные Caddy живут в `/srv/vimovpn/caddy`

## Запуск

1. Настройте runner и secrets.
2. Запустите `Actions -> Deploy Production -> Run workflow` или отправьте commit в `main`.
3. После успешного деплоя панель будет доступна по `https://vimovpn.icu/login`.
