using RelayServer.Hubs;
using RelayServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    // Slightly above the Phase 1 64KB text cap to allow for framing/metadata overhead.
    options.MaximumReceiveMessageSize = 256 * 1024;
}).AddMessagePackProtocol();

builder.Services.AddSingleton<InMemoryClipboardState>();

var app = builder.Build();

app.MapGet("/", () => Results.Text("ClipboardSync RelayServer is running. Connect to /hub/clipboard (SignalR + MessagePack)."));

app.MapHub<ClipboardHub>("/hub/clipboard");

app.Run();
