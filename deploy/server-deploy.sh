#!/usr/bin/env bash
set -Eeuo pipefail

APP_NAME="${APP_NAME:-vimovpn}"
APP_DIR="${APP_DIR:-/srv/vimovpn/app}"
APP_PORT="${APP_PORT:-1488}"
DEPLOY_DOMAIN="${DEPLOY_DOMAIN:-}"
LETSENCRYPT_EMAIL="${LETSENCRYPT_EMAIL:-}"
SHOPBOT_SECRET_KEY="${SHOPBOT_SECRET_KEY:-}"

NGINX_CONF_FILE="/etc/nginx/sites-available/${APP_NAME}.conf"
NGINX_ENABLED_FILE="/etc/nginx/sites-enabled/${APP_NAME}.conf"
WEBROOT="/var/www/certbot"
APT_UPDATED=0
COMPOSE_CMD=()

log() {
    printf '[deploy] %s\n' "$*"
}

fail() {
    printf '[deploy] ERROR: %s\n' "$*" >&2
    exit 1
}

if [ -z "$DEPLOY_DOMAIN" ]; then
    fail "DEPLOY_DOMAIN is required"
fi

if ! [[ "$APP_PORT" =~ ^[0-9]+$ ]]; then
    fail "APP_PORT must be numeric"
fi

if [ "$EUID" -eq 0 ]; then
    SUDO=""
else
    SUDO="sudo"
fi

run_root() {
    if [ -n "$SUDO" ]; then
        "$SUDO" "$@"
    else
        "$@"
    fi
}

apt_update_once() {
    if [ "$APT_UPDATED" -eq 0 ]; then
        run_root apt-get update
        APT_UPDATED=1
    fi
}

ensure_command() {
    local command_name="$1"
    local packages="$2"

    if ! command -v "$command_name" >/dev/null 2>&1; then
        log "Installing packages: $packages"
        apt_update_once
        run_root apt-get install -y $packages
    fi
}

detect_compose() {
    if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
        COMPOSE_CMD=(docker compose)
        return 0
    fi

    if command -v docker-compose >/dev/null 2>&1; then
        COMPOSE_CMD=(docker-compose)
        return 0
    fi

    return 1
}

ensure_compose() {
    if detect_compose; then
        return 0
    fi

    apt_update_once
    if ! run_root apt-get install -y docker-compose-plugin; then
        run_root apt-get install -y docker-compose
    fi

    detect_compose || fail "Docker Compose is not available after installation"
}

compose() {
    if [ -n "$SUDO" ]; then
        run_root "${COMPOSE_CMD[@]}" "$@"
    else
        "${COMPOSE_CMD[@]}" "$@"
    fi
}

ensure_service_enabled() {
    run_root systemctl enable --now "$1"
}

get_server_ip() {
    local ip=""

    for url in \
        "https://api.ipify.org" \
        "https://ifconfig.co/ip" \
        "https://ipv4.icanhazip.com"; do
        ip=$(curl -fsS "$url" 2>/dev/null | tr -d '\r\n\t ')
        if [[ "$ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
            printf '%s\n' "$ip"
            return 0
        fi
    done

    ip=$(hostname -I 2>/dev/null | awk '{print $1}')
    if [[ "$ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
        printf '%s\n' "$ip"
        return 0
    fi

    return 1
}

resolve_domain_ip() {
    local domain="$1"
    local ip=""

    ip=$(getent ahostsv4 "$domain" 2>/dev/null | awk '{print $1}' | head -n1)
    if [[ "$ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
        printf '%s\n' "$ip"
        return 0
    fi

    return 1
}

verify_dns() {
    local server_ip=""
    local domain_ip=""

    server_ip=$(get_server_ip || true)
    domain_ip=$(resolve_domain_ip "$DEPLOY_DOMAIN" || true)

    if [ -n "$server_ip" ] && [ -n "$domain_ip" ] && [ "$server_ip" != "$domain_ip" ]; then
        fail "Domain $DEPLOY_DOMAIN resolves to $domain_ip, but the server public IP is $server_ip"
    fi
}

configure_firewall() {
    if command -v ufw >/dev/null 2>&1 && run_root ufw status | grep -q 'Status: active'; then
        log "Opening ports 80/tcp and 443/tcp in UFW"
        run_root ufw allow 80/tcp
        run_root ufw allow 443/tcp
    fi
}

ensure_system_packages() {
    ensure_command docker "docker.io"
    ensure_command nginx "nginx"
    ensure_command certbot "certbot"
    ensure_command curl "curl"
    ensure_command python3 "python3"

    ensure_service_enabled docker
    ensure_service_enabled nginx
    ensure_compose
}

ensure_runtime_env() {
    local env_file="$APP_DIR/.env"
    local key="$SHOPBOT_SECRET_KEY"
    local tmp_file=""

    if [ -z "$key" ] && [ -f "$env_file" ]; then
        key=$(sed -n 's/^SHOPBOT_SECRET_KEY=//p' "$env_file" | head -n1)
    fi

    if [ -z "$key" ]; then
        key=$(python3 - <<'PY'
import secrets
print(secrets.token_hex(32))
PY
)
    fi

    tmp_file=$(mktemp)
    if [ -f "$env_file" ]; then
        grep -v '^SHOPBOT_SECRET_KEY=' "$env_file" > "$tmp_file" || true
    fi

    printf 'SHOPBOT_SECRET_KEY=%s\n' "$key" >> "$tmp_file"
    mv "$tmp_file" "$env_file"
    chmod 600 "$env_file"
}

seed_setting() {
    local key="$1"
    local value="$2"

    python3 - "$APP_DIR/users.db" "$key" "$value" <<'PY'
from pathlib import Path
import sqlite3
import sys

db_file = Path(sys.argv[1])
key = sys.argv[2]
value = sys.argv[3]

db_file.parent.mkdir(parents=True, exist_ok=True)

with sqlite3.connect(db_file) as conn:
    cursor = conn.cursor()
    cursor.execute(
        """
        CREATE TABLE IF NOT EXISTS bot_settings (
            key TEXT PRIMARY KEY,
            value TEXT
        )
        """
    )
    cursor.execute(
        "INSERT OR REPLACE INTO bot_settings (key, value) VALUES (?, ?)",
        (key, value),
    )
    conn.commit()
PY
}

write_http_bootstrap_config() {
    run_root mkdir -p "$WEBROOT"
    run_root rm -f /etc/nginx/sites-enabled/default

    run_root tee "$NGINX_CONF_FILE" >/dev/null <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name ${DEPLOY_DOMAIN};
    client_max_body_size 20m;

    location /.well-known/acme-challenge/ {
        root ${WEBROOT};
    }

    location / {
        proxy_pass http://127.0.0.1:${APP_PORT};
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

    run_root ln -sfn "$NGINX_CONF_FILE" "$NGINX_ENABLED_FILE"
}

write_https_config() {
    run_root mkdir -p "$WEBROOT"
    run_root rm -f /etc/nginx/sites-enabled/default

    run_root tee "$NGINX_CONF_FILE" >/dev/null <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name ${DEPLOY_DOMAIN};
    client_max_body_size 20m;

    location /.well-known/acme-challenge/ {
        root ${WEBROOT};
    }

    location / {
        return 301 https://\$host\$request_uri;
    }
}

server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name ${DEPLOY_DOMAIN};
    client_max_body_size 20m;

    ssl_certificate /etc/letsencrypt/live/${DEPLOY_DOMAIN}/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/${DEPLOY_DOMAIN}/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    location / {
        proxy_pass http://127.0.0.1:${APP_PORT};
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

    run_root ln -sfn "$NGINX_CONF_FILE" "$NGINX_ENABLED_FILE"
}

ensure_certificate() {
    local cert_file="/etc/letsencrypt/live/${DEPLOY_DOMAIN}/fullchain.pem"

    write_http_bootstrap_config
    run_root nginx -t
    run_root systemctl reload nginx

    if [ ! -f "$cert_file" ]; then
        [ -n "$LETSENCRYPT_EMAIL" ] || fail "LETSENCRYPT_EMAIL is required for the first certificate issue"
        log "Issuing a new Let's Encrypt certificate for ${DEPLOY_DOMAIN}"
        run_root certbot certonly \
            --webroot \
            -w "$WEBROOT" \
            -d "$DEPLOY_DOMAIN" \
            --email "$LETSENCRYPT_EMAIL" \
            --agree-tos \
            --non-interactive
    else
        log "Reusing existing certificate for ${DEPLOY_DOMAIN}"
    fi
}

deploy_application() {
    [ -f "$APP_DIR/docker-compose.yml" ] || fail "docker-compose.yml not found in $APP_DIR"

    mkdir -p "$APP_DIR/backups"
    ensure_runtime_env

    seed_setting "domain" "$DEPLOY_DOMAIN"
    seed_setting "public_base_url" "https://${DEPLOY_DOMAIN}"

    (
        cd "$APP_DIR"
        compose down --remove-orphans || true
        compose up -d --build
    )
}

main() {
    log "Starting deployment for ${DEPLOY_DOMAIN}"
    ensure_system_packages
    configure_firewall
    verify_dns
    ensure_certificate
    write_https_config
    run_root nginx -t
    run_root systemctl reload nginx
    deploy_application
    log "Deployment finished successfully: https://${DEPLOY_DOMAIN}/login"
}

main "$@"
