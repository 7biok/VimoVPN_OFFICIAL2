#!/usr/bin/env bash
set -Eeuo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'

REPO_URL="https://github.com/tweopi/3xui-shopbot.git"
PROJECT_DIR="VimoVPN_OFFICIAL2"
APP_PORT="1488"
NGINX_CONF_FILE="/etc/nginx/sites-available/${PROJECT_DIR}.conf"
NGINX_ENABLED_FILE="/etc/nginx/sites-enabled/${PROJECT_DIR}.conf"
APT_UPDATED=0
ipv4_re='^([0-9]{1,3}\.){3}[0-9]{1,3}$'

handle_error() {
    echo -e "\n${RED}Ошибка на строке $1. Установка прервана.${NC}"
    exit 1
}
trap 'handle_error $LINENO' ERR

read_input() {
    read -r -p "$1" "$2" < /dev/tty
}

read_input_yn() {
    read -r -p "$1" -n 1 REPLY < /dev/tty
    echo
}

apt_update_once() {
    if [ "$APT_UPDATED" -eq 0 ]; then
        sudo apt-get update
        APT_UPDATED=1
    fi
}

install_package() {
    local command_name="$1"
    local packages="$2"
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo -e "${YELLOW}Утилита '$command_name' не найдена. Устанавливаем...${NC}"
        apt_update_once
        sudo apt-get install -y $packages
    else
        echo -e "${GREEN}✓ $command_name уже установлен.${NC}"
    fi
}

ensure_services() {
    for service in docker nginx; do
        if ! sudo systemctl is-active --quiet "$service"; then
            echo -e "${YELLOW}Сервис $service не запущен. Запускаем и добавляем в автозагрузку...${NC}"
            sudo systemctl start "$service"
        fi
        sudo systemctl enable "$service" >/dev/null 2>&1 || true
    done
}

get_server_ip() {
    local ip=""
    for url in \
        "https://api.ipify.org" \
        "https://ifconfig.co/ip" \
        "https://ipv4.icanhazip.com"; do
        ip=$(curl -fsS "$url" 2>/dev/null | tr -d '\r\n\t ')
        if [[ "$ip" =~ $ipv4_re ]]; then
            echo "$ip"
            return 0
        fi
    done

    ip=$(hostname -I 2>/dev/null | awk '{print $1}')
    if [[ "$ip" =~ $ipv4_re ]]; then
        echo "$ip"
        return 0
    fi

    echo ""
}

resolve_domain_ip() {
    local domain="$1"
    local ip=""

    ip=$(getent ahostsv4 "$domain" 2>/dev/null | awk '{print $1}' | head -n1 || true)
    if [[ "$ip" =~ $ipv4_re ]]; then
        echo "$ip"
        return 0
    fi

    if command -v dig >/dev/null 2>&1; then
        ip=$(dig +short A "$domain" 2>/dev/null | grep -E "$ipv4_re" | head -n1 || true)
        if [[ "$ip" =~ $ipv4_re ]]; then
            echo "$ip"
            return 0
        fi
    fi

    if command -v nslookup >/dev/null 2>&1; then
        ip=$(nslookup -type=A "$domain" 2>/dev/null | awk '/^Address: /{print $2; exit}' || true)
        if [[ "$ip" =~ $ipv4_re ]]; then
            echo "$ip"
            return 0
        fi
    fi

    if command -v ping >/dev/null 2>&1; then
        ip=$(ping -4 -c1 -W1 "$domain" 2>/dev/null | sed -n 's/.*(\([0-9.]*\)).*/\1/p' | head -n1 || true)
        if [[ "$ip" =~ $ipv4_re ]]; then
            echo "$ip"
            return 0
        fi
    fi

    echo ""
    return 0
}

configure_firewall() {
    local mode="$1"
    local port="$2"

    if command -v ufw >/dev/null 2>&1 && sudo ufw status | grep -q 'Status: active'; then
        echo -e "${YELLOW}Обнаружен активный ufw. Открываем нужные порты...${NC}"
        sudo ufw allow 80/tcp
        if [ "$mode" = "https" ] && [ "$port" != "80" ]; then
            sudo ufw allow "${port}/tcp"
        fi
    fi
}

write_nginx_http_config() {
    local public_ip="$1"

    sudo rm -f /etc/nginx/sites-enabled/default
    sudo bash -c "cat > '$NGINX_CONF_FILE'" <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name ${public_ip};
    client_max_body_size 20m;

    location / {
        proxy_pass http://127.0.0.1:${APP_PORT};
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

    sudo ln -sfn "$NGINX_CONF_FILE" "$NGINX_ENABLED_FILE"
}

write_nginx_https_config() {
    local domain="$1"
    local https_port="$2"

    sudo rm -f /etc/nginx/sites-enabled/default
    sudo bash -c "cat > '$NGINX_CONF_FILE'" <<EOF
server {
    listen ${https_port} ssl http2;
    listen [::]:${https_port} ssl http2;
    server_name ${domain};
    client_max_body_size 20m;

    ssl_certificate /etc/letsencrypt/live/${domain}/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/${domain}/privkey.pem;

    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_session_timeout 1d;
    ssl_session_cache shared:SSL:10m;
    ssl_session_tickets off;

    location / {
        proxy_pass http://127.0.0.1:${APP_PORT};
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

    sudo ln -sfn "$NGINX_CONF_FILE" "$NGINX_ENABLED_FILE"
}

seed_setting() {
    local key="$1"
    local value="$2"

    python3 - "$key" "$value" <<'PY'
from pathlib import Path
import sqlite3
import sys

key = sys.argv[1]
value = sys.argv[2]
db_file = Path("users.db")

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

echo -e "${GREEN}--- Запуск скрипта установки/обновления 3xui-ShopBot ---${NC}"

if [ -f "$NGINX_CONF_FILE" ]; then
    echo -e "\n${CYAN}Найдена существующая конфигурация. Скрипт запущен в режиме обновления.${NC}"

    if [ ! -d "$PROJECT_DIR" ]; then
        echo -e "${RED}Ошибка: конфигурация Nginx есть, но папка проекта '${PROJECT_DIR}' не найдена.${NC}"
        echo -e "${YELLOW}Удалите $NGINX_CONF_FILE и повторите установку.${NC}"
        exit 1
    fi

    cd "$PROJECT_DIR"

    echo -e "\n${CYAN}Шаг 1: обновление кода из Git...${NC}"
    git pull

    echo -e "\n${CYAN}Шаг 2: пересборка и перезапуск Docker-контейнера...${NC}"
    sudo docker-compose down --remove-orphans
    sudo docker-compose up -d --build

    echo -e "\n${GREEN}Обновление успешно завершено.${NC}"
    exit 0
fi

echo -e "\n${YELLOW}Существующая конфигурация не найдена. Запускается первоначальная установка...${NC}"

echo -e "\n${CYAN}Шаг 1: установка системных зависимостей...${NC}"
install_package "git" "git"
install_package "python3" "python3"
install_package "docker" "docker.io"
install_package "docker-compose" "docker-compose"
install_package "nginx" "nginx"
install_package "curl" "curl"
ensure_services

echo -e "\n${CYAN}Шаг 2: выбор режима установки...${NC}"
echo "1) Домен + HTTPS"
echo "2) Публичный IP + HTTP (без домена и SSL)"
read_input "Введите 1 или 2 [1]: " INSTALL_MODE_INPUT
INSTALL_MODE="${INSTALL_MODE_INPUT:-1}"

case "$INSTALL_MODE" in
    1|2) ;;
    *)
        echo -e "${RED}Неверный режим установки.${NC}"
        exit 1
        ;;
esac

if [ "$INSTALL_MODE" = "1" ]; then
    install_package "certbot" "certbot"
    install_package "dig" "dnsutils"
fi



SERVER_IP=$(get_server_ip)
PUBLIC_BASE_URL=""
PANEL_LOGIN_URL=""
PAYMENT_WEBHOOK_URL=""

if [ "$INSTALL_MODE" = "1" ]; then
    echo -e "\n${CYAN}Шаг 4: настройка домена и HTTPS...${NC}"

    read_input "Введите домен (например, vpn.example.com): " USER_INPUT_DOMAIN
    DOMAIN=$(echo "$USER_INPUT_DOMAIN" \
        | sed -e 's%^https\?://%%' -e 's%/.*$%%' \
        | tr -cd 'A-Za-z0-9.-' \
        | tr '[:upper:]' '[:lower:]')

    if [ -z "$DOMAIN" ]; then
        echo -e "${RED}Ошибка: домен не может быть пустым.${NC}"
        exit 1
    fi

    read_input "Введите email для Let's Encrypt: " EMAIL
    if [ -z "$EMAIL" ]; then
        echo -e "${RED}Ошибка: email не может быть пустым.${NC}"
        exit 1
    fi

    read_input "Введите HTTPS-порт панели (443 или 8443) [443]: " HTTPS_PORT_INPUT
    HTTPS_PORT="${HTTPS_PORT_INPUT:-443}"
    if [ "$HTTPS_PORT" != "443" ] && [ "$HTTPS_PORT" != "8443" ]; then
        echo -e "${RED}Поддерживаются только порты 443 и 8443.${NC}"
        exit 1
    fi

    DOMAIN_IP=$(resolve_domain_ip "$DOMAIN")

    if [ -n "$SERVER_IP" ]; then
        echo -e "${YELLOW}IP сервера: $SERVER_IP${NC}"
    else
        echo -e "${YELLOW}IP сервера автоматически определить не удалось.${NC}"
    fi

    if [ -n "$DOMAIN_IP" ]; then
        echo -e "${YELLOW}IP домена $DOMAIN: $DOMAIN_IP${NC}"
    else
        echo -e "${YELLOW}IP домена $DOMAIN определить не удалось.${NC}"
    fi

    if [ -n "$SERVER_IP" ] && [ -n "$DOMAIN_IP" ] && [ "$SERVER_IP" != "$DOMAIN_IP" ]; then
        echo -e "${RED}DNS-запись домена не указывает на этот сервер.${NC}"
        read_input_yn "Продолжить установку? (y/n): "
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo "Установка прервана."
            exit 1
        fi
    fi

    configure_firewall "https" "$HTTPS_PORT"

    if [ -d "/etc/letsencrypt/live/$DOMAIN" ]; then
        echo -e "${GREEN}Сертификаты для $DOMAIN уже существуют.${NC}"
    else
        echo -e "${YELLOW}Получаем SSL-сертификаты для $DOMAIN...${NC}"
        sudo systemctl stop nginx >/dev/null 2>&1 || true
        sudo certbot certonly --standalone -d "$DOMAIN" --email "$EMAIL" --agree-tos --non-interactive
    fi

    write_nginx_https_config "$DOMAIN" "$HTTPS_PORT"

    if [ "$HTTPS_PORT" = "443" ]; then
        PUBLIC_BASE_URL="https://${DOMAIN}"
    else
        PUBLIC_BASE_URL="https://${DOMAIN}:${HTTPS_PORT}"
    fi
else
    echo -e "\n${CYAN}Шаг 4: настройка установки по IP без HTTPS...${NC}"

    if [ -n "$SERVER_IP" ]; then
        read_input "Введите публичный IP сервера [${SERVER_IP}]: " PUBLIC_IP_INPUT
        PUBLIC_IP="${PUBLIC_IP_INPUT:-$SERVER_IP}"
    else
        read_input "Введите публичный IP сервера: " PUBLIC_IP
    fi

    if [[ ! "$PUBLIC_IP" =~ $ipv4_re ]]; then
        echo -e "${RED}Ошибка: нужно указать корректный IPv4-адрес.${NC}"
        exit 1
    fi

    configure_firewall "http" "80"
    write_nginx_http_config "$PUBLIC_IP"
    PUBLIC_BASE_URL="http://${PUBLIC_IP}"
fi

echo -e "\n${CYAN}Шаг 5: сохраняем публичный URL панели...${NC}"
seed_setting "public_base_url" "$PUBLIC_BASE_URL"

echo -e "\n${CYAN}Шаг 6: проверка и перезапуск Nginx...${NC}"
sudo nginx -t
sudo systemctl restart nginx

echo -e "\n${CYAN}Шаг 7: сборка и запуск Docker-контейнера...${NC}"
if [ "$(sudo docker-compose ps -q)" ]; then
    sudo docker-compose down
fi
sudo docker-compose up -d --build

PANEL_LOGIN_URL="${PUBLIC_BASE_URL}/login"

echo -e "\n${GREEN}=====================================================${NC}"
echo -e "${GREEN}Установка и запуск успешно завершены.${NC}"
echo -e "${GREEN}=====================================================${NC}"
echo -e "\nВеб-панель доступна по адресу:"
echo -e "  - ${YELLOW}${PANEL_LOGIN_URL}${NC}"
echo -e "\nДанные для первого входа:"
echo -e "  - Логин:  ${CYAN}admin${NC}"
echo -e "  - Пароль: ${CYAN}admin${NC}"
echo -e "\nПервые шаги:"
echo -e "1. Войдите в панель и сразу смените логин/пароль."
echo -e "2. В разделе настроек проверьте поле 'Публичный URL панели'."
echo -e "3. Заполните Telegram-параметры и запустите бота."

if [ "$INSTALL_MODE" = "1" ]; then
    PAYMENT_WEBHOOK_URL="${PUBLIC_BASE_URL}/yookassa-webhook"
    echo -e "\nURL для webhook YooKassa:"
    echo -e "  - ${YELLOW}${PAYMENT_WEBHOOK_URL}${NC}"
else
    echo -e "\n${YELLOW}Режим IP + HTTP подходит для панели и работы бота без домена.${NC}"
    echo -e "${YELLOW}Если внешнему платежному сервису нужен HTTPS webhook, позже переключитесь на режим домен + HTTPS.${NC}"
fi

echo
