using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace RelayServer.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/status", (IOptions<AuthOptions> opt) =>
        {
            var o = opt.Value ?? new AuthOptions();
            return Results.Ok(new
            {
                enabled = o.Enabled,
                issuer = o.JwtIssuer,
                audience = o.JwtAudience,
                googleClientIdsConfigured = (o.GoogleClientIds?.Length ?? 0) > 0
            });
        });

        group.MapPost("/google", async (
            [FromBody] GoogleAuthRequest req,
            IGoogleTokenValidator google,
            JwtIssuer jwt,
            IOptions<AuthOptions> opt,
            CancellationToken ct) =>
        {
            if (opt.Value.Enabled != true)
                return Results.BadRequest(new { error = "auth disabled on server" });

            try
            {
                var principal = await google.ValidateAsync(req.IdToken, req.AccessToken, ct);
                var issued = jwt.IssueForGoogleUser(principal.Subject, principal.Email);
                return Results.Ok(new GoogleAuthResponse(
                    Token: issued.token,
                    ExpiresUtcMs: issued.expiresUtc.ToUnixTimeMilliseconds(),
                    Subject: principal.Subject,
                    Email: principal.Email));
            }
            catch (InvalidOperationException ex)
            {
                // Return a clean 400 instead of a 500+stacktrace for expected validation failures.
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/whoami", [Authorize] (ClaimsPrincipal user) =>
        {
            var sub = user.FindFirstValue(JwtIssuer.ClaimSub);
            var email = user.FindFirstValue(JwtIssuer.ClaimEmail);
            return Results.Ok(new { subject = sub, email });
        });
    }
}

public sealed record GoogleAuthRequest(string? IdToken, string? AccessToken);

public sealed record GoogleAuthResponse(string Token, long ExpiresUtcMs, string Subject, string? Email);


