using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
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

    private DesktopAuthSessionDto? _currentSession;
    private string? _accessToken;
    private bool _isAuthenticated;
    private bool _isBusy;
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

    public ObservableCollection<ServerCardModel> ServerCards
    {
        get => _serverCards;
        set => SetField(ref _serverCards, value);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
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
                var errorText = response.Error ?? "unknown_error";
                if (!string.IsNullOrWhiteSpace(response.Details) && !string.Equals(response.Details, errorText, StringComparison.OrdinalIgnoreCase))
                {
                    errorText = $"{errorText}";
                }
                AuthStatusText = $"Не удалось создать код: {errorText}";
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
        ConnectionStatusText = "Отключено";
        SelectedServerName = "Маршрут не выбран";
        SelectedServerDetails = "Туннель остановлен.";
    }

    private async void ResetLogin_Click(object sender, RoutedEventArgs e)
    {
        await _vpnEngine.DisconnectAsync();
        _profileRefreshTimer.Stop();
        _authPollTimer.Stop();
        _authStorage.Clear();
        _accessToken = null;
        _currentSession = null;
        IsAuthenticated = false;
        LoginCodeDisplay = "#--------";
        AuthStatusText = "Вход через Telegram";
        UserDisplayName = "VimoVPN";
        ProfileMetaText = "Ожидание входа";
        TrafficSummaryText = "Трафик появится после загрузки ключей.";
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
            ServerCards = new ObservableCollection<ServerCardModel>(response.Keys.Select(BuildServerCard));
            TrafficSummaryText = BuildTrafficSummary(response.Keys);
            ConnectionStatusText = _vpnEngine.IsRunning ? ConnectionStatusText : "Готов к подключению";
            AuthStatusText = "Вход выполнен";
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

            SelectedServerName = $"{best.SourceKey.HostName} · {best.Endpoint.DisplayName}";
            SelectedServerDetails = $"Ping {(best.PingMs.HasValue ? $"{best.PingMs.Value} мс" : "н/д")} · {best.Endpoint.Protocol}";
            ConnectionStatusText = "Подключено";
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
        return string.Join(" • ", parts);
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
