using System.Net.Http;
using System.Net.Http.Headers;
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
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = JsonSerializer.Deserialize<T>(payload, _jsonOptions);
                return error ?? new T();
            }
            catch
            {
                return new T();
            }
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(payload, _jsonOptions) ?? new T();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
