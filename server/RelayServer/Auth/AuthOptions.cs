namespace RelayServer.Auth;

public sealed class AuthOptions
{
    /// <summary>
    /// When true, RelayServer requires a valid JWT for hub access and enables /auth/* endpoints.
    /// When false, the server behaves as today (roomId+roomSecret required; no auth).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Issuer for RelayServer-issued JWTs.</summary>
    public string JwtIssuer { get; set; } = "ClipboardSync";

    /// <summary>Audience for RelayServer-issued JWTs.</summary>
    public string JwtAudience { get; set; } = "ClipboardSync";

    /// <summary>
    /// HMAC signing key for RelayServer-issued JWTs (HS256). Set via config/secret.
    /// </summary>
    public string JwtSigningKey { get; set; } = "";

    /// <summary>
    /// Allowed Google OAuth client IDs (audiences). Required when Enabled=true.
    /// For desktop apps, this is the client_id inside your downloaded Google OAuth secrets JSON.
    /// </summary>
    public string[] GoogleClientIds { get; set; } = Array.Empty<string>();

    /// <summary>Lifetime for RelayServer-issued JWT access tokens.</summary>
    public int AccessTokenMinutes { get; set; } = 12 * 60;
}


