using System.Diagnostics;
using System.IO;
using System.Text;
using VimoVPN.Client.Models;

namespace VimoVPN.Client.Services;

public sealed class VpnEngineService : IAsyncDisposable
{
    private Process? _process;
    private readonly StringBuilder _logBuffer = new();
    private string? _activeConfigPath;

    public bool IsRunning => _process is { HasExited: false };

    public async Task<(bool Success, string Message)> ConnectAsync(
        SubscriptionEndpoint endpoint,
        string singboxPath,
        CancellationToken cancellationToken)
    {
        await DisconnectAsync();

        if (!File.Exists(singboxPath))
        {
            return (false, $"sing-box.exe not found: {singboxPath}");
        }

        var workingDirectory = Path.GetDirectoryName(singboxPath) ?? AppContext.BaseDirectory;
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VimoVPN",
            "DesktopClient",
            "runtime");
        Directory.CreateDirectory(stateDirectory);

        _activeConfigPath = Path.Combine(stateDirectory, "singbox-config.json");
        File.WriteAllText(_activeConfigPath, SingboxConfigBuilder.BuildConfig(endpoint));

        _logBuffer.Clear();
        var startInfo = new ProcessStartInfo
        {
            FileName = singboxPath,
            Arguments = $"run -c \"{_activeConfigPath}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnProcessOutput;
        _process.ErrorDataReceived += OnProcessOutput;

        if (!_process.Start())
        {
            _process.Dispose();
            _process = null;
            return (false, "Failed to start sing-box process.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        if (_process.HasExited)
        {
            var message = BuildFailureMessage();
            await DisconnectAsync();
            return (false, message);
        }

        return (true, $"Connected via {endpoint.DisplayName}");
    }

    public async Task DisconnectAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
        }
        finally
        {
            _process.OutputDataReceived -= OnProcessOutput;
            _process.ErrorDataReceived -= OnProcessOutput;
            _process.Dispose();
            _process = null;
        }
    }

    public string GetLastLogSnippet()
    {
        return _logBuffer.ToString().Trim();
    }

    private void OnProcessOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        lock (_logBuffer)
        {
            _logBuffer.AppendLine(e.Data.Trim());
            if (_logBuffer.Length > 4000)
            {
                _logBuffer.Remove(0, _logBuffer.Length - 4000);
            }
        }
    }

    private string BuildFailureMessage()
    {
        var logs = GetLastLogSnippet();
        if (string.IsNullOrWhiteSpace(logs))
        {
            return "sing-box exited before tunnel became ready.";
        }

        var lines = logs
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(6);
        return string.Join(Environment.NewLine, lines);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
