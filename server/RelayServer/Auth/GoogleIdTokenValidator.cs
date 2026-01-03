using System.Net.Http.Json;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace RelayServer.Auth;

/// <summary>
/// Validates Google identity for the desktop agent. Supports:
/// - ID token validation (preferred) via GoogleJsonWebSignature
/// - Access token validation via Google's tokeninfo endpoint (fallback; no external SDK required)
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleTokenValidator
{
    private readonly AuthOptions _opt;
    private readonly HttpClient _http;

    public GoogleIdTokenValidator(IOptions<AuthOptions> options, HttpClient http)
    {
        _opt = options.Value ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<GooglePrincipal> ValidateAsync(string? idToken, string? accessToken, CancellationToken ct)
    {
        if (_opt.GoogleClientIds is null || _opt.GoogleClientIds.Length == 0)
            throw new InvalidOperationException("Auth:GoogleClientIds must be configured when auth is enabled.");

        if (!string.IsNullOrWhiteSpace(idToken))
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = _opt.GoogleClientIds
                });

            if (payload is null) throw new InvalidOperationException("Google ID token validation returned empty payload.");
            var sub = payload.Subject ?? "";
            if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("Google ID token did not contain a subject.");
            return new GooglePrincipal(sub, payload.Email);
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            // Tokeninfo endpoint returns basic token metadata. We use it to extract subject + email and validate audience.
            // Ref: https://developers.google.com/identity/sign-in/web/backend-auth (tokeninfo usage patterns)
            var uri = $"https://www.googleapis.com/oauth2/v3/tokeninfo?access_token={Uri.EscapeDataString(accessToken)}";
            var res = await _http.GetFromJsonAsync<TokenInfoResponse>(uri, cancellationToken: ct);
            if (res is null) throw new InvalidOperationException("Google tokeninfo returned empty response.");

            if (string.IsNullOrWhiteSpace(res.Aud) || !_opt.GoogleClientIds.Contains(res.Aud, StringComparer.Ordinal))
                throw new InvalidOperationException("Google access token audience is not allowed.");

            var sub = (res.Sub ?? res.UserId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("Google tokeninfo did not include a subject.");

            return new GooglePrincipal(sub, string.IsNullOrWhiteSpace(res.Email) ? null : res.Email);
        }

        throw new InvalidOperationException("Either idToken or accessToken must be provided.");
    }

    private sealed class TokenInfoResponse
    {
        // tokeninfo fields vary slightly across versions/hosts; we accept a superset.
        public string? Aud { get; set; }
        public string? Sub { get; set; }
        public string? UserId { get; set; }
        public string? Email { get; set; }
    }
}


