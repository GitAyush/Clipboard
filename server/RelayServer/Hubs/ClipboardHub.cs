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
    private const string ClipboardPointerChangedMethod = "ClipboardPointerChanged";
    private const string JoinedRoomItemKey = "roomId";

    private readonly InMemoryClipboardState _state;
    private readonly InMemoryRoomRegistry _rooms;
    private readonly InMemoryPointerState _pointers;
    private readonly ILogger<ClipboardHub> _logger;

    public ClipboardHub(InMemoryClipboardState state, InMemoryRoomRegistry rooms, InMemoryPointerState pointers, ILogger<ClipboardHub> logger)
    {
        _state = state;
        _rooms = rooms;
        _pointers = pointers;
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

    /// <summary>
    /// Phase 2 (pointer-only): join (or create) a room with a shared secret.
    /// Server uses groups to scope pointer broadcasts to that room.
    /// </summary>
    public async Task JoinRoom(string roomId, string roomSecret)
    {
        roomId = (roomId ?? "").Trim();
        roomSecret = (roomSecret ?? "").Trim();

        if (!_rooms.EnsureRoomAndValidateSecret(roomId, roomSecret))
            throw new HubException("invalid roomId/roomSecret.");

        Context.Items[JoinedRoomItemKey] = roomId;
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        _logger.LogInformation("Client joined room. connectionId={ConnectionId} roomId={RoomId}", Context.ConnectionId, roomId);

        // Converge: send latest pointer for this room to the caller.
        var latest = _pointers.GetLatest(roomId);
        if (latest is not null)
        {
            await Clients.Caller.SendAsync(ClipboardPointerChangedMethod, new ClipboardPointerChanged(latest));
        }
    }

    /// <summary>
    /// Phase 2 (pointer-only): publish a pointer to a clipboard item stored outside this server.
    /// No clipboard payload should be sent here.
    /// </summary>
    public async Task ClipboardPointerPublish(ClipboardPointerPublish publish)
    {
        if (publish?.Pointer is null) throw new HubException("pointer is required.");

        var roomId = GetJoinedRoomIdOrThrow();
        if (!string.Equals(roomId, publish.Pointer.RoomId, StringComparison.Ordinal))
            throw new HubException("pointer roomId does not match joined room.");

        // Basic validation (metadata only)
        if (publish.Pointer.OriginDeviceId == Guid.Empty) throw new HubException("originDeviceId is required.");
        if (publish.Pointer.TsUtcMs <= 0) throw new HubException("tsUtcMs is required.");
        if (string.IsNullOrWhiteSpace(publish.Pointer.ObjectKey)) throw new HubException("objectKey is required.");
        if (publish.Pointer.ContentHash is null || publish.Pointer.ContentHash.Length != 32) throw new HubException("contentHash must be 32 bytes.");
        if (publish.Pointer.SizeBytes < 0) throw new HubException("sizeBytes must be >= 0.");
        if (string.IsNullOrWhiteSpace(publish.Pointer.ContentType)) throw new HubException("contentType is required.");

        _pointers.SetLatest(publish.Pointer);

        // Broadcast within room only.
        await Clients.Group(roomId).SendAsync(ClipboardPointerChangedMethod, new ClipboardPointerChanged(publish.Pointer));
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

    private string GetJoinedRoomIdOrThrow()
    {
        if (!Context.Items.TryGetValue(JoinedRoomItemKey, out var value)) throw new HubException("must JoinRoom first.");
        var roomId = value as string;
        if (string.IsNullOrWhiteSpace(roomId)) throw new HubException("must JoinRoom first.");
        return roomId;
    }
}
