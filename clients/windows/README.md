# VimoVPN Windows Client

Простой WPF-клиент для Windows с авторизацией через Telegram и подключением к VPN через `sing-box`.

## Что умеет

- запрашивает одноразовый код входа на backend VimoVPN;
- открывает Telegram-бота с deep link вида `https://t.me/VimoVPN_bot?start=login_CODE`;
- после подтверждения загружает активные ключи пользователя;
- отображает пользователя, лимиты и использованный трафик;
- измеряет ping доступных endpoint-ов и выбирает лучший;
- запускает и останавливает VPN-туннель локально.

## Где проект

- клиент: `clients/windows/VimoVPN.Client`
- backend API для клиента:
  - `POST /desktop-app/auth/start`
  - `GET /desktop-app/auth/status/<session_id>`
  - `GET /desktop-app/me`

## Требования

- Windows 10/11
- .NET SDK 10
- `sing-box.exe`
- `wintun.dll`

## Подготовка runtime

Положите файлы:

- `sing-box.exe`
- `wintun.dll`

в каталог:

- `clients/windows/VimoVPN.Client/runtime/`

После сборки они должны оказаться рядом с приложением в `runtime`.

## Настройки

Файл:

- `clients/windows/VimoVPN.Client/appsettings.json`

Основные параметры:

- `ApiBaseUrl` - публичный URL вашего backend, например `https://vimovpn.icu`
- `ClientName` - имя клиента, которое сохраняется в desktop auth session
- `SingboxRelativePath` - путь до `sing-box.exe` относительно папки приложения

## Сборка

```powershell
$env:DOTNET_CLI_HOME="$PWD\\.dotnet-home"
dotnet build clients\windows\VimoVPN.Client\VimoVPN.Client.csproj -p:RestoreIgnoreFailedSources=true
```

## Release-пакет

Скрипт упаковки:

- `clients/windows/build-release.ps1`

Пример:

```powershell
.\clients\windows\build-release.ps1 -ApiBaseUrl "https://vimovpn.icu"
```

Результат:

- publish-папка в `dist/windows/...`
- zip-архив рядом

По умолчанию пакет framework-dependent. Если нужен self-contained publish, используйте:

```powershell
.\clients\windows\build-release.ps1 -ApiBaseUrl "https://vimovpn.icu" -SelfContained
```

## Запуск

1. Запустите backend VimoVPN.
2. Убедитесь, что у бота настроен публичный username.
3. Соберите клиент.
4. Запустите `VimoVPN.Client.exe` от администратора.
5. Нажмите `Request Code`.
6. Нажмите `Open Telegram`.
7. Подтвердите вход в боте.
8. После загрузки ключей используйте `Connect Best` или `Connect`.

## Ограничения текущей реализации

- клиент сейчас ориентирован на ссылки подписки и direct links форматов:
  - `vless://`
  - `trojan://`
  - `vmess://`
  - `ss://`
- Clash YAML и полноценные sing-box JSON subscriptions пока не разбираются;
- корректное поднятие TUN зависит от наличия `sing-box.exe` и `wintun.dll`;
- для туннеля приложению обычно нужны права администратора.
