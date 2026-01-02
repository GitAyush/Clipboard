using RelayServer.Hubs;
using RelayServer.Services;

namespace RelayServer;

public static class RelayServerApp
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

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

        var app = builder.Build();

        app.MapGet("/", () => Results.Text("ClipboardSync RelayServer is running. Connect to /hub/clipboard (SignalR + MessagePack)."));
        app.MapHub<ClipboardHub>("/hub/clipboard");

        // Relay mode: file download endpoint (payload stored in-memory by room+itemId).
        // Upload path is implemented in a later todo; for now this supports the download contract.
        app.MapGet("/download/{roomId}/{itemId}", (string roomId, string itemId, InMemoryFilePayloadStore files) =>
        {
            var bytes = files.Get(roomId, itemId);
            if (bytes is null) return Results.NotFound();
            return Results.File(bytes, "application/octet-stream", fileDownloadName: itemId);
        });

        return app;
    }
}


