using RelayServer.Hubs;
using RelayServer.Services;
using RelayServer.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace RelayServer;

public static class RelayServerApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

        builder.Services.AddSignalR(options =>
        {
            // Allow larger uploads for file transfer (still bounded by hub validation).
            options.MaximumReceiveMessageSize = 12 * 1024 * 1024;
        }).AddMessagePackProtocol();

        builder.Services.AddSingleton<InMemoryClipboardState>();
        builder.Services.AddSingleton<InMemoryRoomRegistry>();
        builder.Services.AddSingleton<InMemoryPointerState>();
        builder.Services.AddSingleton<InMemoryHistoryState>();
        builder.Services.AddSingleton<InMemoryFilePayloadStore>();

        // Optional auth (enabled via config). We register the pipeline unconditionally so test hosts
        // (WebApplicationFactory) can inject configuration after CreateBuilder() without losing auth.
        // When Auth:Enabled = true:
        // - Clients obtain a RelayServer JWT via POST /auth/google (using Google OAuth tokens)
        // - SignalR connections pass that JWT as "access_token"
        // - Hub access is enforced server-side (see ClipboardHub)
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<JwtIssuer>();
        builder.Services.AddSingleton<IGoogleTokenValidator, GoogleIdTokenValidator>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((o, auth) =>
            {
                var a = auth.Value ?? new AuthOptions();

                // Keep JWT claim names as-is (don't map "sub" -> NameIdentifier, etc.).
                o.MapInboundClaims = false;

                // SignalR sends the bearer token in the query string as "access_token" by convention.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"].ToString();
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hub/clipboard"))
                        {
                            ctx.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };

                if (!a.Enabled)
                {
                    // Auth disabled: keep middleware registered but effectively permissive.
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = false,
                    };
                    return;
                }

                if (string.IsNullOrWhiteSpace(a.JwtSigningKey))
                    throw new InvalidOperationException("Auth enabled but Auth:JwtSigningKey is missing.");

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = a.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = a.JwtAudience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(a.JwtSigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAuthEndpoints();

        app.MapGet("/", () => Results.Text("ClipboardSync RelayServer is running. Connect to /hub/clipboard (SignalR + MessagePack)."));
        app.MapHub<ClipboardHub>("/hub/clipboard");

        // Relay mode: file download endpoint (payload stored in-memory by room+itemId).
        // Upload path is implemented in a later todo; for now this supports the download contract.
        var authEnabled = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Enabled;
        if (authEnabled)
        {
            app.MapGet("/download/{roomId}/{itemId}", (string roomId, string itemId, System.Security.Claims.ClaimsPrincipal user, InMemoryFilePayloadStore files) =>
            {
                var sub = user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? "";
                if (string.IsNullOrWhiteSpace(sub)) return Results.Unauthorized();

                var roomKey = $"{sub}|{(roomId ?? "").Trim()}";
                var bytes = files.Get(roomKey, itemId);
                if (bytes is null) return Results.NotFound();
                return Results.File(bytes, "application/octet-stream", fileDownloadName: itemId);
            }).RequireAuthorization();
        }
        else
        {
            app.MapGet("/download/{roomId}/{itemId}", (string roomId, string itemId, InMemoryFilePayloadStore files) =>
            {
                var bytes = files.Get(roomId, itemId);
                if (bytes is null) return Results.NotFound();
                return Results.File(bytes, "application/octet-stream", fileDownloadName: itemId);
            });
        }

        return app;
    }
}


