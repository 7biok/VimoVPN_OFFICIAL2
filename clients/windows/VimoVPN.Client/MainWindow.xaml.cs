using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using VimoVPN.Client.Models;
using VimoVPN.Client.Services;

namespace VimoVPN.Client;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppConfig _config;
    private readonly AuthStorage _authStorage;
    private readonly DesktopApiClient _apiClient;
    private readonly SubscriptionLinkResolver _subscriptionResolver;
    private readonly VpnEngineService _vpnEngine;
    private readonly DispatcherTimer _authPollTimer;
    private readonly DispatcherTimer _profileRefreshTimer;

    private readonly string _deviceId;
    private readonly string _currentAppVersion;

    private DesktopAuthSessionDto? _currentSession;
    private DesktopKeyDto? _activeKey;
    private SubscriptionEndpoint? _activeEndpoint;
    private string? _accessToken;
    private string? _updateDownloadUrl;

    private bool _isAuthenticated;
    private bool _isBusy;
    private bool _hasUpdateAvailable;
    private string _loginCodeDisplay = "#--------";
    private string _authStatusText = "Вход через Telegram";
    private string _userDisplayName = "VimoVPN";
    private string _profileMetaText = "Ожидание входа";
    private string _trafficSummaryText = "Трафик появится после загрузки ключей.";
    private string _connectionStatusText = "Ожидание входа";
    private string _selectedServerName = "Маршрут не выбран";
    private string _selectedServerDetails = "Лучший сервер будет выбран автоматически.";
    private string _apiBaseUrl;
    private string _telegramHandleText;
    private string _currentVersionText;
    private string _updateStatusText = "Обновления не проверялись";
    private string _updateReleaseNotesText = "Стабильный канал обновлений";
    private string _deviceSummaryText = "Нет активных устройств";
    private ObservableCollection<ServerCardModel> _serverCards = [];

    public MainWindow()
    {
        InitializeComponent();

        _config = AppConfig.Load(AppContext.BaseDirectory);
        _authStorage = new AuthStorage();
        _apiClient = new DesktopApiClient(_config.ApiBaseUrl);
        _subscriptionResolver = new SubscriptionLinkResolver();
        _vpnEngine = new VpnEngineService();
        _authPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, _config.AuthPollIntervalSeconds)) };
        _authPollTimer.Tick += AuthPollTimer_Tick;
        _profileRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(10, _config.ProfileRefreshIntervalSeconds)) };
        _profileRefreshTimer.Tick += ProfileRefreshTimer_Tick;
        _apiBaseUrl = _config.ApiBaseUrl;
        _telegramHandleText = BuildTelegramHandleText(_config.PublicTelegramUrl);
        _deviceId = _authStorage.GetOrCreateDeviceId();
        _currentAppVersion = ResolveAppVersion();
        _currentVersionText = $"v{_currentAppVersion}";

        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => SetField(ref _apiBaseUrl, value);
    }

    public string TelegramHandleText
    {
        get => _telegramHandleText;
        set => SetField(ref _telegramHandleText, value);
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetField(ref _isAuthenticated, value);
    }

    public bool HasUpdateAvailable
    {
        get => _hasUpdateAvailable;
        set => SetField(ref _hasUpdateAvailable, value);
    }

    public string LoginCodeDisplay
    {
        get => _loginCodeDisplay;
        set => SetField(ref _loginCodeDisplay, value);
    }

    public string AuthStatusText
    {
        get => _authStatusText;
        set => SetField(ref _authStatusText, value);
    }

    public string UserDisplayName
    {
        get => _userDisplayName;
        set => SetField(ref _userDisplayName, value);
    }

    public string ProfileMetaText
    {
        get => _profileMetaText;
        set => SetField(ref _profileMetaText, value);
    }

    public string TrafficSummaryText
    {
        get => _trafficSummaryText;
        set => SetField(ref _trafficSummaryText, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set => SetField(ref _connectionStatusText, value);
    }

    public string SelectedServerName
    {
        get => _selectedServerName;
        set => SetField(ref _selectedServerName, value);
    }

    public string SelectedServerDetails
    {
        get => _selectedServerDetails;
        set => SetField(ref _selectedServerDetails, value);
    }

    public string CurrentVersionText
    {
        get => _currentVersionText;
        set => SetField(ref _currentVersionText, value);
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetField(ref _updateStatusText, value);
    }

    public string UpdateReleaseNotesText
    {
        get => _updateReleaseNotesText;
        set => SetField(ref _updateReleaseNotesText, value);
    }

    public string DeviceSummaryText
    {
        get => _deviceSummaryText;
        set => SetField(ref _deviceSummaryText, value);
    }

    public ObservableCollection<ServerCardModel> ServerCards
    {
        get => _serverCards;
        set => SetField(ref _serverCards, value);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(silent: true);

        _accessToken = _authStorage.LoadAccessToken();
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            AuthStatusText = "Восстанавливаю сессию…";
            await LoadProfileAsync(true);
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _authPollTimer.Stop();
        _profileRefreshTimer.Stop();
        await SendHeartbeatAsync(isOnline: false, silent: true);
        await _vpnEngine.DisposeAsync();
        _subscriptionResolver.Dispose();
        _apiClient.Dispose();
    }

    private async void RequestCode_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (TryOpenPendingTelegramSession())
        {
            return;
        }

        try
        {
            _isBusy = true;
            AuthStatusText = "Создаю код входа…";
            var response = await _apiClient.StartAuthSessionAsync(_config.ClientName, CancellationToken.None);
            if (!response.Ok || response.Session is null || string.IsNullOrWhiteSpace(response.Session.SessionId))
            {
                AuthStatusText = $"Не удалось создать код: {response.Error ?? "unknown_error"}";
                return;
            }

            _currentSession = response.Session;
            LoginCodeDisplay = response.Session.DisplayCode ?? BuildDisplayCode(response.Session.Code);
            ConnectionStatusText = "Ожидание подтверждения";
            _authPollTimer.Stop();
            _authPollTimer.Start();
            OpenTelegramForCurrentSession();
        }
        catch (Exception exc)
        {
            AuthStatusText = $"Ошибка авторизации: {exc.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void AuthPollTimer_Tick(object? sender, EventArgs e)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_currentSession?.SessionId))
        {
            return;
        }

        try
        {
            _isBusy = true;
            var status = await _apiClient.GetAuthStatusAsync(_currentSession.SessionId!, CancellationToken.None);
            if (!status.Ok)
            {
                AuthStatusText = $"Ошибка проверки входа: {status.Error ?? "unknown_error"}";
                return;
            }

            var state = (status.Status ?? "pending").Trim().ToLowerInvariant();
            AuthStatusText = state switch
            {
                "bound" => "Вход подтверждён",
                "expired" => "Код истёк",
                "pending" => "Ожидание подтверждения в Telegram",
                _ => $"Статус: {state}",
            };

            if (state == "bound" && !string.IsNullOrWhiteSpace(status.AccessToken))
            {
                _authPollTimer.Stop();
                _accessToken = status.AccessToken;
                _authStorage.SaveAccessToken(status.AccessToken);
                ConnectionStatusText = "Готов к подключению";
                await LoadProfileAsync(false);
            }
            else if (state == "expired")
            {
                _authPollTimer.Stop();
                ConnectionStatusText = "Код истёк";
            }
        }
        catch (Exception exc)
        {
            AuthStatusText = $"Ошибка проверки: {exc.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async void ProfileRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isBusy && IsAuthenticated)
        {
            await LoadProfileAsync(true);
        }
    }

    private async void RefreshProfile_Click(object sender, RoutedEventArgs e)
    {
        await LoadProfileAsync(false);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (HasUpdateAvailable && !string.IsNullOrWhiteSpace(_updateDownloadUrl))
        {
            OpenUpdateDownload_Click(sender, e);
            return;
        }
        await CheckForUpdatesAsync(silent: false);
    }

    private void OpenUpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_updateDownloadUrl))
        {
            UpdateStatusText = "Ссылка на обновление не настроена";
            return;
        }

        try
        {
            OpenExternal(_updateDownloadUrl);
        }
        catch (Exception exc)
        {
            UpdateStatusText = $"Не удалось открыть загрузку: {exc.Message}";
        }
    }

    private async void ConnectBest_Click(object sender, RoutedEventArgs e)
    {
        var connectable = ServerCards.Where(card => card.Key.CanConnect).Select(card => card.Key).ToList();
        if (connectable.Count == 0)
        {
            ConnectionStatusText = "Нет активных ключей для подключения";
            return;
        }

        await ConnectAsync(connectable);
    }

    private async void ConnectSpecific_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is DesktopKeyDto key)
        {
            await ConnectAsync([key]);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await _vpnEngine.DisconnectAsync();
        _activeKey = null;
        _activeEndpoint = null;
        ConnectionStatusText = "Отключено";
        SelectedServerName = "Маршрут не выбран";
        SelectedServerDetails = "Туннель остановлен.";
        await SendHeartbeatAsync(isOnline: true, silent: true);
    }

    private async void ResetLogin_Click(object sender, RoutedEventArgs e)
    {
        await SendHeartbeatAsync(isOnline: false, silent: true);
        await _vpnEngine.DisconnectAsync();
        _profileRefreshTimer.Stop();
        _authPollTimer.Stop();
        _authStorage.Clear();
        _accessToken = null;
        _currentSession = null;
        _activeKey = null;
        _activeEndpoint = null;
        IsAuthenticated = false;
        LoginCodeDisplay = "#--------";
        AuthStatusText = "Вход через Telegram";
        UserDisplayName = "VimoVPN";
        ProfileMetaText = "Ожидание входа";
        TrafficSummaryText = "Трафик появится после загрузки ключей.";
        DeviceSummaryText = "Нет активных устройств";
        ConnectionStatusText = "Ожидание входа";
        SelectedServerName = "Маршрут не выбран";
        SelectedServerDetails = "Лучший сервер будет выбран автоматически.";
        ServerCards = [];
    }

    private async Task LoadProfileAsync(bool silentFailure)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        try
        {
            _isBusy = true;
            var response = await _apiClient.GetProfileAsync(_accessToken, CancellationToken.None);
            if (!response.Ok || response.User is null)
            {
                if (!silentFailure)
                {
                    ConnectionStatusText = $"Ошибка профиля: {response.Error ?? "unknown_error"}";
                }
                if (response.Error is "invalid_token" or "token_expired" or "not_bound")
                {
                    ResetLogin_Click(this, new RoutedEventArgs());
                }
                return;
            }

            IsAuthenticated = true;
            _profileRefreshTimer.Start();
            UserDisplayName = response.User.DisplayName ?? $"user_{response.User.TelegramId}";
            ProfileMetaText = BuildProfileMetaText(response.User);
            DeviceSummaryText = BuildDeviceSummary(response.DeviceSummary);
            ServerCards = new ObservableCollection<ServerCardModel>(response.Keys.Select(BuildServerCard));
            TrafficSummaryText = BuildTrafficSummary(response.Keys);
            ConnectionStatusText = _vpnEngine.IsRunning ? ConnectionStatusText : "Готов к подключению";
            AuthStatusText = "Вход выполнен";
            await SendHeartbeatAsync(isOnline: true, silent: true);
        }
        catch (Exception exc)
        {
            if (!silentFailure)
            {
                ConnectionStatusText = $"Ошибка загрузки: {exc.Message}";
            }
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            var response = await _apiClient.GetUpdateManifestAsync(CancellationToken.None);
            if (!response.Ok)
            {
                if (!silent)
                {
                    UpdateStatusText = $"Ошибка проверки обновлений: {response.Error ?? "unknown_error"}";
                }
                return;
            }

            _updateDownloadUrl = response.DownloadUrl;
            UpdateReleaseNotesText = string.IsNullOrWhiteSpace(response.ReleaseNotes)
                ? "Стабильный канал обновлений"
                : response.ReleaseNotes!;

            if (string.IsNullOrWhiteSpace(response.LatestVersion))
            {
                HasUpdateAvailable = false;
                UpdateStatusText = "Сервер обновлений не отдал версию";
                return;
            }

            var hasUpdate = CompareVersions(response.LatestVersion, _currentAppVersion) > 0;
            HasUpdateAvailable = hasUpdate;
            UpdateStatusText = hasUpdate
                ? $"Доступна версия {response.LatestVersion}"
                : $"Актуальная версия {_currentAppVersion}";
        }
        catch (Exception exc)
        {
            if (!silent)
            {
                UpdateStatusText = $"Проверка обновлений не удалась: {exc.Message}";
            }
        }
    }

    private async Task SendHeartbeatAsync(bool isOnline, bool silent)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        try
        {
            var response = await _apiClient.SendHeartbeatAsync(_accessToken, BuildHeartbeatPayload(isOnline), CancellationToken.None);
            if (!response.Ok && !silent)
            {
                ConnectionStatusText = $"Heartbeat ошибка: {response.Error ?? "unknown_error"}";
            }
        }
        catch (Exception exc)
        {
            if (!silent)
            {
                ConnectionStatusText = $"Heartbeat ошибка: {exc.Message}";
            }
        }
    }

    private DesktopHeartbeatRequest BuildHeartbeatPayload(bool isOnline)
    {
        return new DesktopHeartbeatRequest
        {
            DeviceId = _deviceId,
            ClientName = _config.ClientName,
            DeviceName = "VimoVPN Windows",
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.VersionString,
            AppVersion = _currentAppVersion,
            IsOnline = isOnline,
            VpnConnected = isOnline && _vpnEngine.IsRunning,
            ActiveKeyId = _activeKey?.KeyId,
            ActiveHostName = _activeKey?.HostName,
            ActiveEndpoint = _activeEndpoint is null ? null : $"{_activeEndpoint.Protocol} · {_activeEndpoint.DisplayName}",
        };
    }

    private async Task ConnectAsync(IEnumerable<DesktopKeyDto> sourceKeys)
    {
        if (_isBusy)
        {
            return;
        }

        try
        {
            _isBusy = true;
            ConnectionStatusText = "Получаю доступные узлы…";
            var candidates = new List<ResolvedServerOption>();

            foreach (var key in sourceKeys.Where(item => item.CanConnect && !string.IsNullOrWhiteSpace(item.ConnectionString)))
            {
                var endpoints = await _subscriptionResolver.ResolveAsync(key.ConnectionString!, CancellationToken.None);
                foreach (var endpoint in endpoints)
                {
                    candidates.Add(new ResolvedServerOption
                    {
                        SourceKey = key,
                        Endpoint = endpoint,
                    });
                }
            }

            if (candidates.Count == 0)
            {
                ConnectionStatusText = "Нет поддерживаемых адресов в подписке";
                return;
            }

            ConnectionStatusText = "Проверяю ping…";
            await Task.WhenAll(candidates.Select(async candidate => candidate.PingMs = await PingEndpointAsync(candidate.Endpoint.Server)));
            UpdateServerPings(candidates);

            var best = candidates.Where(item => item.PingMs.HasValue).OrderBy(item => item.PingMs!.Value).FirstOrDefault() ?? candidates.First();
            ConnectionStatusText = $"Подключаю {best.Endpoint.DisplayName}…";

            var singboxPath = _config.ResolveSingboxPath(AppContext.BaseDirectory);
            var result = await _vpnEngine.ConnectAsync(best.Endpoint, singboxPath, CancellationToken.None);
            if (!result.Success)
            {
                ConnectionStatusText = $"Ошибка запуска: {result.Message}";
                return;
            }

            _activeKey = best.SourceKey;
            _activeEndpoint = best.Endpoint;
            SelectedServerName = $"{best.SourceKey.HostName} · {best.Endpoint.DisplayName}";
            SelectedServerDetails = $"Ping {(best.PingMs.HasValue ? $"{best.PingMs.Value} мс" : "н/д")} · {best.Endpoint.Protocol}";
            ConnectionStatusText = "Подключено";
            await SendHeartbeatAsync(isOnline: true, silent: true);
        }
        catch (Exception exc)
        {
            ConnectionStatusText = $"Ошибка подключения: {exc.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private bool TryOpenPendingTelegramSession()
    {
        if (IsAuthenticated || _currentSession is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_currentSession.Code))
        {
            return false;
        }

        if (_currentSession.CodeExpiresAtTimestampMs is long expiresAtTimestampMs &&
            expiresAtTimestampMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            return false;
        }

        if (!_authPollTimer.IsEnabled)
        {
            _authPollTimer.Start();
        }

        OpenTelegramForCurrentSession();
        return true;
    }

    private void OpenTelegramForCurrentSession()
    {
        var code = _currentSession?.Code;
        var authUrl = BuildTelegramAuthUrl(_config.PublicTelegramUrl, _currentSession?.BotUrl, code);
        if (string.IsNullOrWhiteSpace(authUrl))
        {
            AuthStatusText = "Не удалось сформировать ссылку Telegram";
            return;
        }

        var loginCommand = (_currentSession?.LoginCommand ?? BuildLoginCommand(code)).Trim();
        var copied = TryCopyToClipboard(loginCommand);
        try
        {
            OpenExternal(authUrl);
        }
        catch (Exception exc)
        {
            AuthStatusText = $"Не удалось открыть Telegram: {exc.Message}";
            return;
        }
        AuthStatusText = copied
            ? $"Открыт Telegram. Команда {loginCommand} скопирована."
            : "Открыт Telegram.";
    }

    private void UpdateServerPings(IEnumerable<ResolvedServerOption> candidates)
    {
        var bestByKey = candidates
            .GroupBy(item => item.SourceKey.KeyId)
            .ToDictionary(group => group.Key, group => group.Where(item => item.PingMs.HasValue).OrderBy(item => item.PingMs!.Value).FirstOrDefault());

        var updated = ServerCards.Select(card =>
        {
            if (bestByKey.TryGetValue(card.Key.KeyId, out var best) && best?.PingMs is long ping)
            {
                card.PingText = $"Лучший ping: {ping} мс";
            }
            else
            {
                card.PingText = "Ping будет проверен при подключении";
            }
            return card;
        }).ToList();

        ServerCards = new ObservableCollection<ServerCardModel>(updated);
    }

    private static async Task<long?> PingEndpointAsync(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static ServerCardModel BuildServerCard(DesktopKeyDto key)
    {
        return new ServerCardModel
        {
            Key = key,
            StatusText = key.IsExpired
                ? "Истёк"
                : (!key.IsEnabled
                    ? "Отключён на панели"
                    : (key.CanConnect ? $"Готов · {key.PanelType ?? "panel"}" : "Нет ссылки подключения")),
            TrafficText = FormatTraffic(key.UsedBytes, key.LimitBytes),
            PingText = string.IsNullOrWhiteSpace(key.PingTarget) ? "Ping будет проверен при подключении" : $"Узел: {key.PingTarget}",
        };
    }

    private static string BuildProfileMetaText(DesktopUserSummaryDto user)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            parts.Add("@" + user.Username.Trim());
        }
        parts.Add($"ID {user.TelegramId}");
        parts.Add($"Ключей {user.KeysCount}");
        parts.Add($"Устройств {user.DeviceCount}");
        return string.Join(" • ", parts);
    }

    private static string BuildDeviceSummary(DesktopDeviceSummaryDto? summary)
    {
        if (summary is null || summary.TotalDevices <= 0)
        {
            return "Устройства появятся после первой авторизации клиента.";
        }

        return $"{summary.TotalDevices} устройств · {summary.OnlineDevices} онлайн · {summary.ConnectedDevices} с VPN";
    }

    private static string BuildTrafficSummary(IEnumerable<DesktopKeyDto> keys)
    {
        var used = keys.Where(item => item.UsedBytes.HasValue).Sum(item => item.UsedBytes ?? 0);
        var limit = keys.Where(item => item.LimitBytes.HasValue).Sum(item => item.LimitBytes ?? 0);
        if (used <= 0 && limit <= 0)
        {
            return "Трафик пока не получен";
        }
        if (limit <= 0)
        {
            return $"Использовано {FormatBytes(used)}";
        }
        return $"Использовано {FormatBytes(used)} из {FormatBytes(limit)}";
    }

    private static string FormatTraffic(long? usedBytes, long? limitBytes)
    {
        if (usedBytes is null && limitBytes is null)
        {
            return "Трафик не получен";
        }
        if ((limitBytes ?? 0) <= 0)
        {
            return $"Использовано {FormatBytes(usedBytes ?? 0)}";
        }
        return $"{FormatBytes(usedBytes ?? 0)} / {FormatBytes(limitBytes ?? 0)}";
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = Math.Max(0, value);
        var order = 0;
        double normalized = size;
        while (normalized >= 1024 && order < units.Length - 1)
        {
            order += 1;
            normalized /= 1024;
        }
        return $"{normalized:0.##} {units[order]}";
    }

    private static string BuildDisplayCode(string? code)
    {
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(cleanCode) ? "#--------" : $"#{cleanCode}";
    }

    private static string BuildLoginCommand(string? code)
    {
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(cleanCode) ? "/login=" : $"/login={cleanCode}";
    }

    private static string BuildTelegramAuthUrl(string? publicTelegramUrl, string? fallbackBotUrl, string? code)
    {
        var cleanCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var baseUrl = !string.IsNullOrWhiteSpace(fallbackBotUrl)
            ? fallbackBotUrl!.Trim()
            : (publicTelegramUrl ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(cleanCode))
        {
            return baseUrl;
        }

        if (baseUrl.Contains("start=", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl;
        }

        if (string.IsNullOrWhiteSpace(fallbackBotUrl))
        {
            return baseUrl;
        }

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}start=login_{Uri.EscapeDataString(cleanCode)}";
    }

    private static string BuildTelegramHandleText(string? publicTelegramUrl)
    {
        var value = (publicTelegramUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "t.me/vimovpn";
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return $"{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        }

        return value.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
    }

    private static string ResolveAppVersion()
    {
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "1.0.0" : assemblyVersion;
    }

    private static int CompareVersions(string left, string right)
    {
        left = NormalizeVersion(left);
        right = NormalizeVersion(right);
        if (!Version.TryParse(left, out var leftVersion))
        {
            leftVersion = new Version(0, 0, 0, 0);
        }
        if (!Version.TryParse(right, out var rightVersion))
        {
            rightVersion = new Version(0, 0, 0, 0);
        }
        return leftVersion.CompareTo(rightVersion);
    }

    private static string NormalizeVersion(string rawVersion)
    {
        var segments = (rawVersion ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => int.TryParse(segment, out _))
            .Take(4)
            .ToList();

        while (segments.Count < 4)
        {
            segments.Add("0");
        }

        return segments.Count == 0 ? "0.0.0.0" : string.Join('.', segments);
    }

    private static bool TryCopyToClipboard(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenExternal(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
