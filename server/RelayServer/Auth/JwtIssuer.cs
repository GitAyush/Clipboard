using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace RelayServer.Auth;

public sealed class JwtIssuer
{
    public const string ClaimSub = JwtRegisteredClaimNames.Sub;
    public const string ClaimEmail = JwtRegisteredClaimNames.Email;

    private readonly AuthOptions _opt;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtIssuer(IOptions<AuthOptions> options)
    {
        _opt = options.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public (string token, DateTimeOffset expiresUtc) IssueForGoogleUser(string googleSubject, string? email)
    {
        if (string.IsNullOrWhiteSpace(googleSubject)) throw new ArgumentException("googleSubject is required.", nameof(googleSubject));
        if (string.IsNullOrWhiteSpace(_opt.JwtSigningKey)) throw new InvalidOperationException("Auth:JwtSigningKey must be set when auth is enabled.");

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(Math.Clamp(_opt.AccessTokenMinutes, 5, 7 * 24 * 60));

        var claims = new List<Claim>
        {
            new(ClaimSub, googleSubject),
        };

        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new Claim(ClaimEmail, email));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.JwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opt.JwtIssuer,
            audience: _opt.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (_handler.WriteToken(jwt), expires);
    }
}


