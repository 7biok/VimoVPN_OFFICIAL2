using System.IO;
using System.Text.Json;

namespace VimoVPN.Client.Services;

public sealed class AuthStorage
{
    private readonly string _stateFilePath;

    public AuthStorage()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VimoVPN",
            "DesktopClient");
        Directory.CreateDirectory(root);
        _stateFilePath = Path.Combine(root, "auth.json");
    }

    public string? LoadAccessToken()
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<AuthState>(json);
            return string.IsNullOrWhiteSpace(state?.AccessToken) ? null : state.AccessToken;
        }
        catch
        {
            return null;
        }
    }

    public void SaveAccessToken(string accessToken)
    {
        var payload = JsonSerializer.Serialize(new AuthState { AccessToken = accessToken });
        File.WriteAllText(_stateFilePath, payload);
    }

    public void Clear()
    {
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }
    }

    private sealed class AuthState
    {
        public string? AccessToken { get; set; }
    }
}
