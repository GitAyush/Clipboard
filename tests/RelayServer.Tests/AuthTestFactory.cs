using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RelayServer.Auth;
using Microsoft.Extensions.Configuration;

namespace RelayServer.Tests;

public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "true",
                ["Auth:JwtIssuer"] = "TestIssuer",
                ["Auth:JwtAudience"] = "TestAudience",
                ["Auth:JwtSigningKey"] = "test-signing-key-please-change",
                ["Auth:AccessTokenMinutes"] = "60",
                ["Auth:GoogleClientIds:0"] = "test-client-id"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<AuthOptions>(o =>
            {
                o.Enabled = true;
                o.JwtIssuer = "TestIssuer";
                o.JwtAudience = "TestAudience";
                // Must be >= 256 bits for HS256 in Microsoft.IdentityModel
                o.JwtSigningKey = "0123456789abcdef0123456789abcdef";
                o.AccessTokenMinutes = 60;
                o.GoogleClientIds = new[] { "test-client-id" };
            });

            // Replace real Google validator (which would call out to Google) with a deterministic fake.
            services.AddSingleton<IGoogleTokenValidator, FakeGoogleTokenValidator>();
        });
    }

    private sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
    {
        public Task<GooglePrincipal> ValidateAsync(string? idToken, string? accessToken, CancellationToken ct)
        {
            // For tests, treat the provided token string as the subject.
            var sub = (idToken ?? accessToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sub)) throw new InvalidOperationException("missing token");
            return Task.FromResult(new GooglePrincipal(sub, $"{sub}@example.com"));
        }
    }
}


