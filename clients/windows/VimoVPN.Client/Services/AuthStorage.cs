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
        var state = LoadState();
        return string.IsNullOrWhiteSpace(state?.AccessToken) ? null : state.AccessToken;
    }

    public void SaveAccessToken(string accessToken)
    {
        var state = LoadState() ?? new AuthState();
        state.AccessToken = accessToken;
        if (string.IsNullOrWhiteSpace(state.DeviceId))
        {
            state.DeviceId = Guid.NewGuid().ToString("N");
        }
        SaveState(state);
    }

    public string GetOrCreateDeviceId()
    {
        var state = LoadState() ?? new AuthState();
        if (string.IsNullOrWhiteSpace(state.DeviceId))
        {
            state.DeviceId = Guid.NewGuid().ToString("N");
            SaveState(state);
        }
        return state.DeviceId!;
    }

    public void Clear()
    {
        var state = LoadState();
        if (state is null)
        {
            return;
        }
        state.AccessToken = null;
        if (string.IsNullOrWhiteSpace(state.DeviceId))
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }
            return;
        }
        SaveState(state);
    }

    private AuthState? LoadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<AuthState>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SaveState(AuthState state)
    {
        var payload = JsonSerializer.Serialize(state);
        File.WriteAllText(_stateFilePath, payload);
    }

    private sealed class AuthState
    {
        public string? AccessToken { get; set; }
        public string? DeviceId { get; set; }
    }
}
