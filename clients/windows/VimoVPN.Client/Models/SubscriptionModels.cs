namespace VimoVPN.Client.Models;

public sealed class SubscriptionEndpoint
{
    public required string Protocol { get; init; }
    public required string OriginalLink { get; init; }
    public required string Server { get; init; }
    public required int ServerPort { get; init; }
    public required string DisplayName { get; init; }
    public string? Credential { get; init; }
    public string? Password { get; init; }
    public string? Method { get; init; }
    public string? Security { get; init; }
    public string? Sni { get; init; }
    public string? HostHeader { get; init; }
    public string? Path { get; init; }
    public string? ServiceName { get; init; }
    public string? Flow { get; init; }
    public string? PublicKey { get; init; }
    public string? ShortId { get; init; }
    public string? Fingerprint { get; init; }
    public string? Network { get; init; }
    public string? VmessSecurity { get; init; }
    public int? AlterId { get; init; }
}

public sealed class ResolvedServerOption
{
    public required DesktopKeyDto SourceKey { get; init; }
    public required SubscriptionEndpoint Endpoint { get; init; }
    public long? PingMs { get; set; }
}

public sealed class ServerCardModel
{
    public required DesktopKeyDto Key { get; init; }
    public string Title => string.IsNullOrWhiteSpace(Key.HostName) ? $"Key #{Key.KeyId}" : Key.HostName!;
    public string TrafficText { get; set; } = "No traffic data";
    public string StatusText { get; set; } = "Unknown";
    public string PingText { get; set; } = "Ping not tested";
}
