using System.Text;
using ClipboardSync.Protocol;
using Microsoft.AspNetCore.SignalR;
using RelayServer.Services;

namespace RelayServer.Hubs;

/// <summary>
/// Phase 1: global clipboard relay hub (no auth, no encryption, no persistence).
/// Clients publish clipboard text; server broadcasts <c>ClipboardChanged</c> to all connected clients.
/// </summary>
public sealed class ClipboardHub : Hub
{
    private const string ClipboardChangedMethod = "ClipboardChanged";

    private readonly InMemoryClipboardState _state;
    private readonly ILogger<ClipboardHub> _logger;

    public ClipboardHub(InMemoryClipboardState state, ILogger<ClipboardHub> logger)
    {
        _state = state;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

        // Push latest value to new connections so they immediately converge.
        var latest = _state.GetLatest();
        if (latest is not null)
        {
            await Clients.Caller.SendAsync(ClipboardChangedMethod, latest);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(exception, "Client disconnected with error: {ConnectionId}", Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client → Server: publish local clipboard text.
    /// </summary>
    public async Task ClipboardPublish(ClipboardPublish publish)
    {
        var computedHash = ValidateAndComputeHash(publish);

        var changed = new ClipboardChanged(
            OriginDeviceId: publish.DeviceId,
            ServerTsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text: publish.Text,
            TextHash: computedHash);

        _state.SetLatest(changed);

        // Broadcast to all clients (including origin). Clients prevent loops by ignoring originDeviceId == local deviceId.
        await Clients.All.SendAsync(ClipboardChangedMethod, changed);
    }

    private byte[] ValidateAndComputeHash(ClipboardPublish publish)
    {
        if (publish is null) throw new HubException("payload is required.");
        if (publish.DeviceId == Guid.Empty) throw new HubException("deviceId is required.");
        if (publish.ClientItemId == Guid.Empty) throw new HubException("clientItemId is required.");
        if (publish.Text is null) throw new HubException("text is required.");

        // Payload limit: 64KB UTF-8 bytes.
        if (Encoding.UTF8.GetByteCount(publish.Text) > ClipboardProtocol.MaxTextBytesUtf8)
        {
            throw new HubException($"text exceeds max of {ClipboardProtocol.MaxTextBytesUtf8} UTF-8 bytes.");
        }

        var computed = ClipboardProtocol.ComputeTextHashUtf8(publish.Text);
        if (publish.TextHash is not null && publish.TextHash.Length == 32 && ClipboardProtocol.HashEquals(publish.TextHash, computed))
        {
            return publish.TextHash;
        }

        _logger.LogDebug(
            "Client hash missing/mismatched; using computed hash. connectionId={ConnectionId} deviceId={DeviceId} clientItemId={ClientItemId}",
            Context.ConnectionId, publish.DeviceId, publish.ClientItemId);

        return computed;
    }
}
