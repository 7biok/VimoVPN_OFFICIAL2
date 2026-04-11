using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VimoVPN.Client.Models;

namespace VimoVPN.Client.Services;

public sealed class DesktopApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public DesktopApiClient(string apiBaseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VimoVPN-Windows/1.0");
    }

    public async Task<AuthSessionStartResponse> StartAuthSessionAsync(string clientName, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { client_name = clientName });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("desktop-app/auth/start", content, cancellationToken);
        return await DeserializeAsync<AuthSessionStartResponse>(response, cancellationToken);
    }

    public async Task<AuthStatusResponse> GetAuthStatusAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"desktop-app/auth/status/{Uri.EscapeDataString(sessionId)}", cancellationToken);
        return await DeserializeAsync<AuthStatusResponse>(response, cancellationToken);
    }

    public async Task<DesktopProfileResponse> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "desktop-app/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await DeserializeAsync<DesktopProfileResponse>(response, cancellationToken);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : new()
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        T result;

        if (string.IsNullOrWhiteSpace(payload))
        {
            result = new T();
        }
        else
        {
            try
            {
                result = JsonSerializer.Deserialize<T>(payload, _jsonOptions) ?? new T();
            }
            catch
            {
                result = new T();
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = ExtractErrorMessage(response, payload);
            SetStringPropertyIfExists(result, "Error", errorMessage);
            SetStringPropertyIfExists(result, "Details", payload);
        }

        return result;
    }

    private static void SetStringPropertyIfExists<T>(T target, string propertyName, string? value)
    {
        if (target is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var property = typeof(T).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || property.PropertyType != typeof(string) || !property.CanWrite)
        {
            return;
        }

        var currentValue = property.GetValue(target) as string;
        if (!string.IsNullOrWhiteSpace(currentValue))
        {
            return;
        }

        property.SetValue(target, value);
    }

    private static string ExtractErrorMessage(HttpResponseMessage response, string? payload)
    {
        var fallback = $"http_{(int)response.StatusCode}";
        var text = (payload ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                var errorValue = errorElement.GetString();
                if (!string.IsNullOrWhiteSpace(errorValue))
                {
                    if (root.TryGetProperty("details", out var detailsElement))
                    {
                        var detailsValue = detailsElement.GetString();
                        if (!string.IsNullOrWhiteSpace(detailsValue))
                        {
                            return $"{errorValue}: {detailsValue}";
                        }
                    }
                    return errorValue;
                }
            }

            if (root.TryGetProperty("message", out var messageElement))
            {
                var messageValue = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(messageValue))
                {
                    return messageValue;
                }
            }
        }
        catch
        {
        }

        var compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (compact.Length > 220)
        {
            compact = compact[..220].Trim() + "...";
        }

        return string.IsNullOrWhiteSpace(compact) ? fallback : $"{fallback}: {compact}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
