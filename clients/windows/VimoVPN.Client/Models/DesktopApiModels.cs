using System.Text.Json.Serialization;

namespace VimoVPN.Client.Models;

public sealed class AuthSessionStartResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("session")]
    public DesktopAuthSessionDto? Session { get; set; }
}

public sealed class AuthStatusResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

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

    [JsonPropertyName("user")]
    public DesktopUserSummaryDto? User { get; set; }

    [JsonPropertyName("keys")]
    public List<DesktopKeyDto> Keys { get; set; } = [];

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
