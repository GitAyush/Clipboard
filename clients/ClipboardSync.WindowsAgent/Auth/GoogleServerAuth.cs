using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Net.Http.Json;

namespace ClipboardSync.WindowsAgent.Auth;

[ExcludeFromCodeCoverage]
public static class GoogleServerAuth
{
    public sealed record AuthResult(string Token, DateTimeOffset ExpiresUtc, string Subject, string? Email);

    public sealed record ServerAuthStatus(bool Enabled, bool GoogleClientIdsConfigured, string? Issuer, string? Audience);

    public static async Task<ServerAuthStatus> GetServerAuthStatusAsync(string serverBaseUrl, CancellationToken ct)
    {
        var baseUrl = (serverBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("ServerBaseUrl is required.");

        using var http = new HttpClient();
        var url = $"{baseUrl}/auth/status";
        var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }
            throw new InvalidOperationException(
                $"Server auth status check failed ({(int)resp.StatusCode} {resp.ReasonPhrase}) at {url}. Response: {body}");
        }

        var payload = await resp.Content.ReadFromJsonAsync<ServerAuthStatusResponse>(cancellationToken: ct);
        if (payload is null) throw new InvalidOperationException("Auth status response was empty.");

        return new ServerAuthStatus(
            Enabled: payload.enabled,
            GoogleClientIdsConfigured: payload.googleClientIdsConfigured,
            Issuer: payload.issuer,
            Audience: payload.audience);
    }

    public static async Task<AuthResult> LoginAsync(string serverBaseUrl, string? idToken, string? accessToken, CancellationToken ct)
    {
        var baseUrl = (serverBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new InvalidOperationException("ServerBaseUrl is required.");
        if (string.IsNullOrWhiteSpace(idToken) && string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Either Google idToken or accessToken is required.");

        using var http = new HttpClient();
        var url = $"{baseUrl}/auth/google";
        var resp = await http.PostAsJsonAsync(url, new { idToken, accessToken }, cancellationToken: ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = "";
            try { body = await resp.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }
            throw new InvalidOperationException($"Server auth failed ({(int)resp.StatusCode} {resp.ReasonPhrase}) at {url}. Response: {body}");
        }

        var payload = await resp.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
        if (payload is null) throw new InvalidOperationException("Auth response was empty.");
        if (string.IsNullOrWhiteSpace(payload.Token)) throw new InvalidOperationException("Auth response token was empty.");

        var expires = DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresUtcMs);
        return new AuthResult(payload.Token, expires, payload.Subject ?? "", payload.Email);
    }

    private sealed class AuthResponse
    {
        public string Token { get; set; } = "";
        public long ExpiresUtcMs { get; set; }
        public string? Subject { get; set; }
        public string? Email { get; set; }
    }

    private sealed class ServerAuthStatusResponse
    {
        public bool enabled { get; set; }
        public string? issuer { get; set; }
        public string? audience { get; set; }
        public bool googleClientIdsConfigured { get; set; }
    }
}


