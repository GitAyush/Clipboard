using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RelayServer.Tests;

public sealed class RelayServerAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // IMPORTANT: Force auth OFF for non-auth integration tests.
        // Developers may have Auth enabled locally via appsettings.Development.json or env vars;
        // without this, JoinRoom would require JWT and many tests would fail.
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Enabled"] = "false"
            });
        });
    }
}


