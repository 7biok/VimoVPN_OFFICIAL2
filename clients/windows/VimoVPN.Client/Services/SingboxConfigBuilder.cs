using System.Text.Json;
using VimoVPN.Client.Models;

namespace VimoVPN.Client.Services;

public static class SingboxConfigBuilder
{
    public static string BuildConfig(SubscriptionEndpoint endpoint)
    {
        var root = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = "info",
                ["timestamp"] = true,
            },
            ["dns"] = new Dictionary<string, object?>
            {
                ["servers"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["tag"] = "remote",
                        ["address"] = "https://1.1.1.1/dns-query",
                        ["detour"] = "proxy",
                        ["strategy"] = "ipv4_only",
                    },
                    new Dictionary<string, object?>
                    {
                        ["tag"] = "local",
                        ["address"] = "local",
                    },
                },
                ["strategy"] = "ipv4_only",
                ["independent_cache"] = true,
                ["final"] = "remote",
            },
            ["inbounds"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "VimoVPN",
                    ["address"] = new[] { "172.19.0.1/30", "fdfe:dcba:9876::1/126" },
                    ["mtu"] = 1408,
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    ["sniff"] = true,
                    ["sniff_override_destination"] = true,
                    ["stack"] = "mixed",
                },
            },
            ["outbounds"] = new object[]
            {
                BuildOutbound(endpoint),
                new Dictionary<string, object?>
                {
                    ["type"] = "direct",
                    ["tag"] = "direct",
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "block",
                    ["tag"] = "block",
                },
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["auto_detect_interface"] = true,
                ["rules"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["protocol"] = "dns",
                        ["outbound"] = "proxy",
                    },
                    new Dictionary<string, object?>
                    {
                        ["network"] = "udp",
                        ["port"] = 53,
                        ["outbound"] = "proxy",
                    },
                },
                ["final"] = "proxy",
            },
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private static Dictionary<string, object?> BuildOutbound(SubscriptionEndpoint endpoint)
    {
        return endpoint.Protocol switch
        {
            "vless" => BuildVless(endpoint),
            "trojan" => BuildTrojan(endpoint),
            "vmess" => BuildVmess(endpoint),
            "shadowsocks" => BuildShadowsocks(endpoint),
            _ => throw new NotSupportedException($"Unsupported protocol: {endpoint.Protocol}"),
        };
    }

    private static Dictionary<string, object?> BuildVless(SubscriptionEndpoint endpoint)
    {
        var outbound = BuildBaseOutbound("vless", endpoint);
        outbound["uuid"] = endpoint.Credential;
        if (!string.IsNullOrWhiteSpace(endpoint.Flow))
        {
            outbound["flow"] = endpoint.Flow;
        }
        outbound["packet_encoding"] = "xudp";
        AppendTlsAndTransport(outbound, endpoint);
        return outbound;
    }

    private static Dictionary<string, object?> BuildTrojan(SubscriptionEndpoint endpoint)
    {
        var outbound = BuildBaseOutbound("trojan", endpoint);
        outbound["password"] = endpoint.Credential;
        outbound["packet_encoding"] = "xudp";
        AppendTlsAndTransport(outbound, endpoint, forceTls: true);
        return outbound;
    }

    private static Dictionary<string, object?> BuildVmess(SubscriptionEndpoint endpoint)
    {
        var outbound = BuildBaseOutbound("vmess", endpoint);
        outbound["uuid"] = endpoint.Credential;
        outbound["security"] = string.IsNullOrWhiteSpace(endpoint.VmessSecurity) ? "auto" : endpoint.VmessSecurity;
        if (endpoint.AlterId is int alterId && alterId > 0)
        {
            outbound["alter_id"] = alterId;
        }
        outbound["packet_encoding"] = "packetaddr";
        AppendTlsAndTransport(outbound, endpoint);
        return outbound;
    }

    private static Dictionary<string, object?> BuildShadowsocks(SubscriptionEndpoint endpoint)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "shadowsocks",
            ["tag"] = "proxy",
            ["server"] = endpoint.Server,
            ["server_port"] = endpoint.ServerPort,
            ["method"] = endpoint.Method,
            ["password"] = endpoint.Password,
        };
    }

    private static Dictionary<string, object?> BuildBaseOutbound(string type, SubscriptionEndpoint endpoint)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = type,
            ["tag"] = "proxy",
            ["server"] = endpoint.Server,
            ["server_port"] = endpoint.ServerPort,
        };
    }

    private static void AppendTlsAndTransport(Dictionary<string, object?> outbound, SubscriptionEndpoint endpoint, bool forceTls = false)
    {
        var security = (endpoint.Security ?? string.Empty).Trim().ToLowerInvariant();
        var tlsEnabled = forceTls || security is "tls" or "xtls" or "reality";
        if (tlsEnabled)
        {
            var tls = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["server_name"] = string.IsNullOrWhiteSpace(endpoint.Sni)
                    ? (string.IsNullOrWhiteSpace(endpoint.HostHeader) ? endpoint.Server : endpoint.HostHeader)
                    : endpoint.Sni,
                ["insecure"] = endpoint.AllowInsecureTls,
            };
            if (!string.IsNullOrWhiteSpace(endpoint.Fingerprint))
            {
                tls["utls"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["fingerprint"] = endpoint.Fingerprint,
                };
            }
            if (security == "reality")
            {
                tls["reality"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["public_key"] = endpoint.PublicKey,
                    ["short_id"] = endpoint.ShortId ?? string.Empty,
                };
            }
            outbound["tls"] = tls;
        }

        var network = (endpoint.Network ?? string.Empty).Trim().ToLowerInvariant();
        if (network is "ws" or "websocket")
        {
            var transport = new Dictionary<string, object?>
            {
                ["type"] = "ws",
                ["path"] = string.IsNullOrWhiteSpace(endpoint.Path) ? "/" : endpoint.Path,
            };
            if (!string.IsNullOrWhiteSpace(endpoint.HostHeader))
            {
                transport["headers"] = new Dictionary<string, object?>
                {
                    ["Host"] = endpoint.HostHeader,
                };
            }
            outbound["transport"] = transport;
        }
        else if (network is "grpc")
        {
            outbound["transport"] = new Dictionary<string, object?>
            {
                ["type"] = "grpc",
                ["service_name"] = string.IsNullOrWhiteSpace(endpoint.ServiceName) ? "grpc" : endpoint.ServiceName,
            };
        }
        else if (network is "httpupgrade")
        {
            outbound["transport"] = new Dictionary<string, object?>
            {
                ["type"] = "httpupgrade",
                ["path"] = string.IsNullOrWhiteSpace(endpoint.Path) ? "/" : endpoint.Path,
                ["host"] = endpoint.HostHeader ?? endpoint.Server,
            };
        }
    }
}
