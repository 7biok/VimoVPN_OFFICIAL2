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
    private string _loginCodeDisplay = "--------";
    private string _authStatusText = "Request a code to start Telegram authorization.";
    private string _userDisplayName = "Not authorized";
    private string _profileMetaText = "Waiting for login";
    private string _trafficSummaryText = "Traffic will appear after loading active keys.";
    private string _connectionStatusText = "Disconnected";
    private string _selectedServerName = "No active tunnel";
    private string _selectedServerDetails = "The client will select the best endpoint after ping check.";
    private string _apiBaseUrl;
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

        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => SetField(ref _apiBaseUrl, value);
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

        try
        {
            _isBusy = true;
            AuthStatusText = "Requesting a login code from VimoVPN backend...";
            var response = await _apiClient.StartAuthSessionAsync(_config.ClientName, CancellationToken.None);
            if (!response.Ok || response.Session is null || string.IsNullOrWhiteSpace(response.Session.SessionId))
            {
                AuthStatusText = $"Failed to request code: {response.Error ?? "unknown_error"}";
                return;
            }

            _currentSession = response.Session;
            LoginCodeDisplay = response.Session.DisplayCode ?? response.Session.Code ?? "--------";
            AuthStatusText = "Open Telegram and confirm the login in the bot. The app is polling for authorization.";
            _authPollTimer.Start();
        }
        catch (Exception exc)
        {
            AuthStatusText = $"Auth session request failed: {exc.Message}";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void OpenTelegram_Click(object sender, RoutedEventArgs e)
    {
        var url = _currentSession?.BotUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            AuthStatusText = "Request a code first, then open Telegram.";
            return;
        }

        OpenExternal(url);
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
                AuthStatusText = $"Authorization polling failed: {status.Error ?? "unknown_error"}";
                return;
            }

            var state = (status.Status ?? "pending").Trim().ToLowerInvariant();
            AuthStatusText = state switch
            {
                "bound" => "Login confirmed. Loading profile...",
                "expired" => "The login code expired. Request a new one.",
                "pending" => "Waiting for confirmation in Telegram...",
                _ => $"Current state: {state}",
            };

            if (state == "bound" && !string.IsNullOrWhiteSpace(status.AccessToken))
            {
                _authPollTimer.Stop();
                _accessToken = status.AccessToken;
                _authStorage.SaveAccessToken(status.AccessToken);
                await LoadProfileAsync(false);
            }
            else if (state == "expired")
            {
                _authPollTimer.Stop();
            }
        }
        catch (Exception exc)
        {
            AuthStatusText = $"Authorization polling error: {exc.Message}";
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
            ConnectionStatusText = "No active subscriptions are ready for connection.";
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
        ConnectionStatusText = "Disconnected";
        SelectedServerName = "No active tunnel";
        SelectedServerDetails = "The tunnel was stopped and routes were released.";
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
        LoginCodeDisplay = "--------";
        AuthStatusText = "Request a code to start Telegram authorization.";
        UserDisplayName = "Not authorized";
        ProfileMetaText = "Waiting for login";
        TrafficSummaryText = "Traffic will appear after loading active keys.";
        ConnectionStatusText = "Disconnected";
        SelectedServerName = "No active tunnel";
        SelectedServerDetails = "The client will select the best endpoint after ping check.";
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
                    ConnectionStatusText = $"Profile loading failed: {response.Error ?? "unknown_error"}";
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
            ProfileMetaText = $"Telegram ID {response.User.TelegramId}  |  Balance {response.User.BalanceRub:0.00} RUB  |  Keys {response.User.KeysCount}";
            ServerCards = new ObservableCollection<ServerCardModel>(response.Keys.Select(BuildServerCard));
            TrafficSummaryText = BuildTrafficSummary(response.Keys);
            ConnectionStatusText = _vpnEngine.IsRunning ? ConnectionStatusText : "Disconnected";
        }
        catch (Exception exc)
        {
            if (!silentFailure)
            {
                ConnectionStatusText = $"Profile loading error: {exc.Message}";
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
            ConnectionStatusText = "Resolving subscription endpoints...";
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
                ConnectionStatusText = "No supported endpoints were found in the selected subscriptions.";
                return;
            }

            ConnectionStatusText = "Measuring ping to available endpoints...";
            await Task.WhenAll(candidates.Select(async candidate => candidate.PingMs = await PingEndpointAsync(candidate.Endpoint.Server)));
            UpdateServerPings(candidates);

            var best = candidates.Where(item => item.PingMs.HasValue).OrderBy(item => item.PingMs!.Value).FirstOrDefault() ?? candidates.First();
            ConnectionStatusText = $"Starting tunnel via {best.Endpoint.DisplayName}...";
            var singboxPath = _config.ResolveSingboxPath(AppContext.BaseDirectory);
            var result = await _vpnEngine.ConnectAsync(best.Endpoint, singboxPath, CancellationToken.None);
            if (!result.Success)
            {
                ConnectionStatusText = $"Tunnel start failed: {result.Message}";
                return;
            }

            SelectedServerName = $"{best.SourceKey.HostName} -> {best.Endpoint.DisplayName}";
            SelectedServerDetails = $"Best ping: {(best.PingMs.HasValue ? $"{best.PingMs.Value} ms" : "n/a")}  |  Protocol: {best.Endpoint.Protocol}";
            ConnectionStatusText = result.Message;
        }
        catch (Exception exc)
        {
            ConnectionStatusText = $"Connection failed: {exc.Message}";
        }
        finally
        {
            _isBusy = false;
        }
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
                card.PingText = $"Best endpoint ping: {ping} ms";
            }
            else
            {
                card.PingText = "Best endpoint ping: unavailable";
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
            StatusText = key.IsExpired ? "Expired" : (!key.IsEnabled ? "Disabled on panel" : (key.CanConnect ? $"Ready to connect via {key.PanelType ?? "panel"}" : "No connection string available")),
            TrafficText = FormatTraffic(key.UsedBytes, key.LimitBytes),
            PingText = string.IsNullOrWhiteSpace(key.PingTarget) ? "Ping target unavailable" : $"Ping target: {key.PingTarget}",
        };
    }

    private static string BuildTrafficSummary(IEnumerable<DesktopKeyDto> keys)
    {
        var used = keys.Where(item => item.UsedBytes.HasValue).Sum(item => item.UsedBytes ?? 0);
        var limit = keys.Where(item => item.LimitBytes.HasValue).Sum(item => item.LimitBytes ?? 0);
        if (used <= 0 && limit <= 0)
        {
            return "Traffic: no panel metrics yet";
        }
        return $"Traffic: {FormatBytes(used)} used of {FormatBytes(limit)}";
    }

    private static string FormatTraffic(long? usedBytes, long? limitBytes)
    {
        if (usedBytes is null && limitBytes is null)
        {
            return "Traffic: no data";
        }
        return $"Traffic: {FormatBytes(usedBytes ?? 0)} / {FormatBytes(limitBytes ?? 0)}";
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
