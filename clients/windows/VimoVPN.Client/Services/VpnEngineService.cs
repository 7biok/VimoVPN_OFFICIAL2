using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using VimoVPN.Client.Models;

namespace VimoVPN.Client.Services;

public sealed class VpnEngineService : IAsyncDisposable
{
    private Process? _process;
    private readonly StringBuilder _logBuffer = new();

    public bool IsRunning => _process is { HasExited: false };
    public string? LastConfigPath { get; private set; }

    public async Task<(bool Success, string Message)> ConnectAsync(
        SubscriptionEndpoint endpoint,
        string singboxPath,
        CancellationToken cancellationToken)
    {
        await DisconnectAsync();

        var precheckError = ValidateRuntimePrerequisites(singboxPath);
        if (!string.IsNullOrWhiteSpace(precheckError))
        {
            return (false, precheckError);
        }

        var workingDirectory = Path.GetDirectoryName(singboxPath) ?? AppContext.BaseDirectory;
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VimoVPN",
            "DesktopClient",
            "runtime");
        Directory.CreateDirectory(stateDirectory);

        LastConfigPath = Path.Combine(stateDirectory, "singbox-config.json");
        File.WriteAllText(LastConfigPath, SingboxConfigBuilder.BuildConfig(endpoint));

        var configCheck = await CheckConfigAsync(singboxPath, LastConfigPath, cancellationToken);
        if (!configCheck.Success)
        {
            return (false, configCheck.Message);
        }

        _logBuffer.Clear();
        var startInfo = new ProcessStartInfo
        {
            FileName = singboxPath,
            Arguments = $"run -c \"{LastConfigPath}\"",
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
            return (false, "Не удалось запустить процесс sing-box.");
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        for (var attempt = 0; attempt < 12; attempt += 1)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            if (_process.HasExited)
            {
                var message = BuildFailureMessage();
                await DisconnectAsync();
                return (false, message);
            }
        }

        return (true, $"Туннель поднят через {endpoint.DisplayName}");
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
        lock (_logBuffer)
        {
            return _logBuffer.ToString().Trim();
        }
    }

    private static string? ValidateRuntimePrerequisites(string singboxPath)
    {
        if (!File.Exists(singboxPath))
        {
            return $"Не найден runtime: {singboxPath}";
        }

        var workingDirectory = Path.GetDirectoryName(singboxPath) ?? AppContext.BaseDirectory;
        var wintunPath = Path.Combine(workingDirectory, "wintun.dll");
        if (!File.Exists(wintunPath))
        {
            return $"Не найден wintun.dll рядом с sing-box: {wintunPath}";
        }

        if (OperatingSystem.IsWindows() && !IsProcessElevated())
        {
            return "Клиент нужно запускать от имени администратора для TUN-режима.";
        }

        return null;
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool Success, string Message)> CheckConfigAsync(
        string singboxPath,
        string configPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = singboxPath,
                Arguments = $"check -c \"{configPath}\"",
                WorkingDirectory = Path.GetDirectoryName(singboxPath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        };

        if (!process.Start())
        {
            return (false, "sing-box check не удалось запустить.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var combined = $"{await outputTask}{Environment.NewLine}{await errorTask}".Trim();
        if (process.ExitCode == 0)
        {
            return (true, combined);
        }

        if (string.IsNullOrWhiteSpace(combined))
        {
            combined = $"sing-box check завершился с кодом {process.ExitCode}.";
        }

        return (false, $"Конфиг sing-box не прошёл проверку.{Environment.NewLine}{combined}{Environment.NewLine}Конфиг: {configPath}");
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
            if (_logBuffer.Length > 6000)
            {
                _logBuffer.Remove(0, _logBuffer.Length - 6000);
            }
        }
    }

    private string BuildFailureMessage()
    {
        var logs = GetLastLogSnippet();
        if (string.IsNullOrWhiteSpace(logs))
        {
            return $"sing-box завершился сразу после запуска. Проверьте конфиг: {LastConfigPath}";
        }

        var lines = logs
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(8);
        return $"{string.Join(Environment.NewLine, lines)}{Environment.NewLine}Конфиг: {LastConfigPath}";
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
