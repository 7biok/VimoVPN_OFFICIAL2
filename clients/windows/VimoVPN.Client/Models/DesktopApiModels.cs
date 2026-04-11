using System.Text.Json.Serialization;

namespace VimoVPN.Client.Models;

public sealed class AuthSessionStartResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("session")]
    public DesktopAuthSessionDto? Session { get; set; }
}

public sealed class AuthStatusResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("display_code")]
    public string? DisplayCode { get; set; }

    [JsonPropertyName("bot_url")]
    public string? BotUrl { get; set; }

    [JsonPropertyName("user")]
    public DesktopUserSummaryDto? User { get; set; }
}

public sealed class DesktopAuthSessionDto
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("display_code")]
    public string? DisplayCode { get; set; }

    [JsonPropertyName("bot_url")]
    public string? BotUrl { get; set; }

    [JsonPropertyName("login_command")]
    public string? LoginCommand { get; set; }

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("status_url")]
    public string? StatusUrl { get; set; }

    [JsonPropertyName("code_expires_at_timestamp_ms")]
    public long? CodeExpiresAtTimestampMs { get; set; }

    [JsonPropertyName("token_expires_at_timestamp_ms")]
    public long? TokenExpiresAtTimestampMs { get; set; }
}

public sealed class DesktopProfileResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("user")]
    public DesktopUserSummaryDto? User { get; set; }

    [JsonPropertyName("keys")]
    public List<DesktopKeyDto> Keys { get; set; } = [];

    [JsonPropertyName("devices")]
    public List<DesktopDeviceDto> Devices { get; set; } = [];

    [JsonPropertyName("device_summary")]
    public DesktopDeviceSummaryDto? DeviceSummary { get; set; }

    [JsonPropertyName("generated_at_timestamp_ms")]
    public long? GeneratedAtTimestampMs { get; set; }
}

public sealed class DesktopUserSummaryDto
{
    [JsonPropertyName("telegram_id")]
    public long TelegramId { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("balance_rub")]
    public double BalanceRub { get; set; }

    [JsonPropertyName("keys_count")]
    public int KeysCount { get; set; }

    [JsonPropertyName("device_count")]
    public int DeviceCount { get; set; }

    [JsonPropertyName("online_device_count")]
    public int OnlineDeviceCount { get; set; }

    [JsonPropertyName("connected_device_count")]
    public int ConnectedDeviceCount { get; set; }
}

public sealed class DesktopKeyDto
{
    [JsonPropertyName("key_id")]
    public int KeyId { get; set; }

    [JsonPropertyName("host_name")]
    public string? HostName { get; set; }

    [JsonPropertyName("key_email")]
    public string? KeyEmail { get; set; }

    [JsonPropertyName("xui_client_uuid")]
    public string? ClientUuid { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("expiry_timestamp_ms")]
    public long? ExpiryTimestampMs { get; set; }

    [JsonPropertyName("connection_string")]
    public string? ConnectionString { get; set; }

    [JsonPropertyName("server_url")]
    public string? ServerUrl { get; set; }

    [JsonPropertyName("ping_target")]
    public string? PingTarget { get; set; }

    [JsonPropertyName("used_bytes")]
    public long? UsedBytes { get; set; }

    [JsonPropertyName("limit_bytes")]
    public long? LimitBytes { get; set; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("is_expired")]
    public bool IsExpired { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("panel_type")]
    public string? PanelType { get; set; }

    [JsonPropertyName("can_connect")]
    public bool CanConnect { get; set; }
}

public sealed class DesktopDeviceDto
{
    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("device_name")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("machine_name")]
    public string? MachineName { get; set; }

    [JsonPropertyName("os_version")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("app_version")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("vpn_connected")]
    public bool VpnConnected { get; set; }

    [JsonPropertyName("active_key_id")]
    public int? ActiveKeyId { get; set; }

    [JsonPropertyName("active_host_name")]
    public string? ActiveHostName { get; set; }

    [JsonPropertyName("active_endpoint")]
    public string? ActiveEndpoint { get; set; }

    [JsonPropertyName("state_label")]
    public string? StateLabel { get; set; }

    [JsonPropertyName("last_seen_at")]
    public string? LastSeenAt { get; set; }
}

public sealed class DesktopDeviceSummaryDto
{
    [JsonPropertyName("total_devices")]
    public int TotalDevices { get; set; }

    [JsonPropertyName("online_devices")]
    public int OnlineDevices { get; set; }

    [JsonPropertyName("connected_devices")]
    public int ConnectedDevices { get; set; }

    [JsonPropertyName("total_connections")]
    public int TotalConnections { get; set; }
}

public sealed class DesktopHeartbeatRequest
{
    [JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("device_name")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("machine_name")]
    public string? MachineName { get; set; }

    [JsonPropertyName("os_version")]
    public string? OsVersion { get; set; }

    [JsonPropertyName("app_version")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("vpn_connected")]
    public bool VpnConnected { get; set; }

    [JsonPropertyName("active_key_id")]
    public int? ActiveKeyId { get; set; }

    [JsonPropertyName("active_host_name")]
    public string? ActiveHostName { get; set; }

    [JsonPropertyName("active_endpoint")]
    public string? ActiveEndpoint { get; set; }
}

public sealed class DesktopHeartbeatResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("device")]
    public DesktopDeviceDto? Device { get; set; }

    [JsonPropertyName("server_time_timestamp_ms")]
    public long? ServerTimeTimestampMs { get; set; }
}

public sealed class DesktopUpdateManifestResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}
