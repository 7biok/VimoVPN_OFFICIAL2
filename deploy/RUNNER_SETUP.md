# Self-Hosted Runner Setup

Эта схема использует любой `self-hosted` runner, доступный репозиторию.

## 1. Подготовьте пользователя

```bash
sudo adduser --disabled-password --gecos "" github-runner
sudo usermod -aG docker github-runner
sudo mkdir -p /srv/vimovpn/data/backups /srv/vimovpn/caddy/data /srv/vimovpn/caddy/config
sudo chown -R github-runner:github-runner /srv/vimovpn
```

## 2. Установите runner

Дальше зайдите в GitHub:

`Repository -> Settings -> Actions -> Runners -> New self-hosted runner`

На сервере выполните команды, которые покажет GitHub, от пользователя `github-runner`.

Важно:

- ставьте runner именно для этого репозитория;
- запускайте runner как service, а не в интерактивной сессии.

## 3. Минимальные требования

- Docker Engine
- Docker Compose plugin
- доступ в интернет для GitHub и Let's Encrypt
- открытые `80/tcp` и `443/tcp`

## 4. Проверка

После установки runner должен появиться в списке репозитория как `Idle`.
