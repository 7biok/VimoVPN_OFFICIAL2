using System.IO;
using System.Text.Json;

namespace VimoVPN.Client.Services;

public sealed class AppConfig
{
    public string ApiBaseUrl { get; set; } = "https://vimovpn.icu";
    public string ClientName { get; set; } = "VimoVPN Windows";
    public string SingboxRelativePath { get; set; } = @"runtime\sing-box.exe";
    public int AuthPollIntervalSeconds { get; set; } = 2;
    public int ProfileRefreshIntervalSeconds { get; set; } = 20;

    public static AppConfig Load(string baseDirectory)
    {
        var configPath = Path.Combine(baseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public string ResolveSingboxPath(string baseDirectory)
    {
        if (Path.IsPathRooted(SingboxRelativePath))
        {
            return SingboxRelativePath;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, SingboxRelativePath));
    }
}
