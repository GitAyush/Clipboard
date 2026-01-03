namespace RelayServer.Auth;

public sealed record GooglePrincipal(string Subject, string? Email);

public interface IGoogleTokenValidator
{
    /// <summary>
    /// Validate either a Google ID token or an OAuth access token and return the Google subject (stable account id) and email (if available).
    /// </summary>
    Task<GooglePrincipal> ValidateAsync(string? idToken, string? accessToken, CancellationToken ct);
}


