# GitHub Deploy для `vimovpn.icu`

Этот репозиторий настроен на автоматический деплой через GitHub Actions runner `ubuntu-latest` с выкладкой по SSH на сервер `2.26.108.92`.

## Что делает workflow

- Запускается при push в `main` или вручную через `workflow_dispatch`.
- Синхронизирует код в `/srv/vimovpn/app`.
- Не удаляет рабочие данные: `users.db`, `backups/`, `.env`.
- На сервере автоматически ставит `docker`, `nginx`, `certbot`, если их еще нет.
- Выпускает или переиспользует SSL для `vimovpn.icu`.
- Пересобирает и перезапускает контейнеры через Docker Compose.

## Нужные GitHub Secrets

Добавьте в `Settings -> Secrets and variables -> Actions`:

- `DEPLOY_USER`: пользователь SSH на сервере. Обычно `root`.
- `DEPLOY_SSH_KEY`: приватный SSH-ключ для входа на сервер.
- `LETSENCRYPT_EMAIL`: email для выпуска первого SSL-сертификата.
- `SHOPBOT_SECRET_KEY`: необязательный секрет для Flask-сессий. Если не задан, сервер сгенерирует его сам и сохранит в `/srv/vimovpn/app/.env`.

## Важные условия

- Домен `vimovpn.icu` должен смотреть на `2.26.108.92`.
- Если `DEPLOY_USER` не `root`, у него должен быть `passwordless sudo` для `apt`, `systemctl`, `nginx`, `certbot`, `docker`.
- Порт `22` должен быть доступен для GitHub Actions runner'ов.

## Первый запуск

1. Добавьте secrets.
2. Убедитесь, что сервер принимает SSH по ключу.
3. Запустите `Actions -> Deploy Production -> Run workflow` или сделайте push в `main`.
4. После завершения панель будет доступна по `https://vimovpn.icu/login`.

## Где хранится прод

- Код: `/srv/vimovpn/app`
- База: `/srv/vimovpn/app/users.db`
- Бэкапы: `/srv/vimovpn/app/backups`
- Nginx config: `/etc/nginx/sites-available/vimovpn.conf`
