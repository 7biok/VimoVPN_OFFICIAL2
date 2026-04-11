using System.Net.Http;
using System.Text;
using System.Text.Json;
using VimoVPN.Client.Models;

namespace VimoVPN.Client.Services;

public sealed class SubscriptionLinkResolver : IDisposable
{
    private static readonly string[] SupportedSchemes = ["vless://", "vmess://", "trojan://", "ss://"];
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public async Task<List<SubscriptionEndpoint>> ResolveAsync(string connectionString, CancellationToken cancellationToken)
    {
        var text = (connectionString ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (IsDirectLink(text))
        {
            return ParseLinks([text]);
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return [];
        }

        var payload = await _httpClient.GetStringAsync(uri, cancellationToken);
        var body = NormalizeSubscriptionBody(payload);
        var links = ExtractLinks(body);
        return ParseLinks(links);
    }

    private static bool IsDirectLink(string value)
    {
        return SupportedSchemes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeSubscriptionBody(string payload)
    {
        var text = payload.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            return text;
        }

        try
        {
            var normalized = text.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
            }
            var bytes = Convert.FromBase64String(normalized);
            var decoded = Encoding.UTF8.GetString(bytes);
            if (decoded.Contains("://", StringComparison.Ordinal))
            {
                return decoded;
            }
        }
        catch
        {
        }

        return text;
    }

    private static IEnumerable<string> ExtractLinks(string body)
    {
        return body
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsDirectLink)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static List<SubscriptionEndpoint> ParseLinks(IEnumerable<string> links)
    {
        var result = new List<SubscriptionEndpoint>();
        foreach (var link in links)
        {
            var endpoint = ParseLink(link);
            if (endpoint is not null)
            {
                result.Add(endpoint);
            }
        }
        return result;
    }

    private static SubscriptionEndpoint? ParseLink(string link)
    {
        if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVlessOrTrojan(link, "vless");
        }
        if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVlessOrTrojan(link, "trojan");
        }
        if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseVmess(link);
        }
        if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseShadowsocks(link);
        }
        return null;
    }

    private static SubscriptionEndpoint? ParseVlessOrTrojan(string link, string protocol)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.Port <= 0)
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var label = BuildLabel(uri.Fragment, uri.Host, protocol);
        var network = GetQueryValue(query, "type") ?? "tcp";

        return new SubscriptionEndpoint
        {
            Protocol = protocol,
            OriginalLink = link,
            Server = uri.Host,
            ServerPort = uri.Port,
            DisplayName = label,
            Credential = Uri.UnescapeDataString(uri.UserInfo),
            Security = GetQueryValue(query, "security"),
            Sni = GetQueryValue(query, "sni") ?? GetQueryValue(query, "serverName"),
            HostHeader = GetQueryValue(query, "host"),
            Path = NormalizePath(GetQueryValue(query, "path")),
            ServiceName = GetQueryValue(query, "serviceName"),
            Flow = GetQueryValue(query, "flow"),
            PublicKey = GetQueryValue(query, "pbk"),
            ShortId = GetQueryValue(query, "sid"),
            Fingerprint = GetQueryValue(query, "fp"),
            AllowInsecureTls = ParseBoolean(GetQueryValue(query, "allowInsecure"))
                || ParseBoolean(GetQueryValue(query, "insecure"))
                || ParseBoolean(GetQueryValue(query, "skip-cert-verify")),
            Network = network,
        };
    }

    private static SubscriptionEndpoint? ParseVmess(string link)
    {
        var encoded = link["vmess://".Length..].Trim();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            var normalized = encoded.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var payload = JsonSerializer.Deserialize<VmessPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (payload is null || string.IsNullOrWhiteSpace(payload.add) || payload.port <= 0 || string.IsNullOrWhiteSpace(payload.id))
            {
                return null;
            }

            return new SubscriptionEndpoint
            {
                Protocol = "vmess",
                OriginalLink = link,
                Server = payload.add,
                ServerPort = payload.port,
                DisplayName = string.IsNullOrWhiteSpace(payload.ps) ? payload.add : payload.ps,
                Credential = payload.id,
                Security = string.Equals(payload.tls, "tls", StringComparison.OrdinalIgnoreCase) ? "tls" : payload.tls,
                Sni = payload.sni,
                HostHeader = payload.host,
                Path = NormalizePath(payload.path),
                ServiceName = payload.path,
                Network = string.IsNullOrWhiteSpace(payload.net) ? "tcp" : payload.net,
                VmessSecurity = string.IsNullOrWhiteSpace(payload.scy) ? "auto" : payload.scy,
                AlterId = payload.aid,
            };
        }
        catch
        {
            return null;
        }
    }

    private static SubscriptionEndpoint? ParseShadowsocks(string link)
    {
        var payload = link["ss://".Length..];
        var fragmentIndex = payload.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? payload[(fragmentIndex + 1)..] : string.Empty;
        var working = fragmentIndex >= 0 ? payload[..fragmentIndex] : payload;

        if (string.IsNullOrWhiteSpace(working))
        {
            return null;
        }

        var atIndex = working.LastIndexOf('@');
        string credentials;
        string hostPart;
        if (atIndex >= 0)
        {
            credentials = working[..atIndex];
            hostPart = working[(atIndex + 1)..];
        }
        else
        {
            var slashIndex = working.IndexOf('/');
            var encoded = slashIndex >= 0 ? working[..slashIndex] : working;
            var remainder = slashIndex >= 0 ? working[slashIndex..] : string.Empty;
            var decoded = DecodeBase64String(encoded);
            if (string.IsNullOrWhiteSpace(decoded) || !decoded.Contains('@'))
            {
                return null;
            }
            var decodedAt = decoded.LastIndexOf('@');
            credentials = decoded[..decodedAt];
            hostPart = decoded[(decodedAt + 1)..] + remainder;
        }

        if (credentials.Contains(':') is false)
        {
            var decoded = DecodeBase64String(credentials);
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                credentials = decoded;
            }
        }

        var credentialParts = credentials.Split(':', 2);
        if (credentialParts.Length != 2)
        {
            return null;
        }

        hostPart = hostPart.Split('?', 2)[0].Trim();
        var hostPort = hostPart.Split(':', 2);
        if (hostPort.Length != 2 || !int.TryParse(hostPort[1], out var port) || port <= 0)
        {
            return null;
        }

        return new SubscriptionEndpoint
        {
            Protocol = "shadowsocks",
            OriginalLink = link,
            Server = hostPort[0],
            ServerPort = port,
            DisplayName = BuildLabel(fragment, hostPort[0], "shadowsocks"),
            Method = credentialParts[0],
            Password = credentialParts[1],
            Network = "tcp",
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = (query ?? string.Empty).Trim().TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string? GetQueryValue(Dictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "/";
        }
        return rawPath.StartsWith('/') ? rawPath : "/" + rawPath;
    }

    private static string BuildLabel(string fragment, string fallback, string protocol)
    {
        var label = Uri.UnescapeDataString((fragment ?? string.Empty).Trim().TrimStart('#'));
        return string.IsNullOrWhiteSpace(label) ? $"{fallback} ({protocol})" : label;
    }

    private static string? DecodeBase64String(string value)
    {
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch
        {
            return null;
        }
    }

    private static bool ParseBoolean(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class VmessPayload
    {
        public string? add { get; set; }
        public int port { get; set; }
        public string? id { get; set; }
        public int? aid { get; set; }
        public string? net { get; set; }
        public string? host { get; set; }
        public string? path { get; set; }
        public string? tls { get; set; }
        public string? sni { get; set; }
        public string? scy { get; set; }
        public string? ps { get; set; }
    }
}
