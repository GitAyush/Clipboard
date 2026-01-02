using System.Text;
using System.Security.Cryptography;
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
    private const string HistoryItemAddedMethod = "HistoryItemAdded";
    private const string JoinedRoomItemKey = "roomId";

    private readonly InMemoryClipboardState _state;
    private readonly InMemoryRoomRegistry _rooms;
    private readonly InMemoryPointerState _pointers;
    private readonly InMemoryHistoryState _history;
    private readonly InMemoryFilePayloadStore _files;
    private readonly ILogger<ClipboardHub> _logger;

    public ClipboardHub(InMemoryClipboardState state, InMemoryRoomRegistry rooms, InMemoryPointerState pointers, InMemoryHistoryState history, InMemoryFilePayloadStore files, ILogger<ClipboardHub> logger)
    {
        _state = state;
        _rooms = rooms;
        _pointers = pointers;
        _history = history;
        _files = files;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);

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
        var roomId = GetJoinedRoomIdOrThrow();
        var computedHash = ValidateAndComputeHash(publish);

        var changed = new ClipboardChanged(
            OriginDeviceId: publish.DeviceId,
            ServerTsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text: publish.Text,
            TextHash: computedHash);

        _state.SetLatest(roomId, changed);

        // Add to per-room history (text only for now).
        var id = $"{changed.ServerTsUtcMs}_{publish.DeviceId:N}_{ClipboardProtocol.HashToHex(computedHash)}";
        var preview = publish.Text.Length <= 120 ? publish.Text : publish.Text.Substring(0, 120);
        var item = new HistoryItem(
            Id: id,
            RoomId: roomId,
            Kind: HistoryItemKind.Text,
            OriginDeviceId: publish.DeviceId,
            TsUtcMs: changed.ServerTsUtcMs,
            Title: preview,
            Preview: preview,
            SizeBytes: Encoding.UTF8.GetByteCount(publish.Text),
            ContentHash: computedHash,
            ContentType: "text/plain",
            ProviderFileId: null,
            ObjectKey: null);

        _history.Append(roomId, item, maxItems: 10);
        _history.PutTextPayload(roomId, id, publish.Text);

        // Broadcast within room only.
        await Clients.Group(roomId).SendAsync(ClipboardChangedMethod, changed);
        await Clients.Group(roomId).SendAsync(HistoryItemAddedMethod, new HistoryItemAdded(item));
    }

    /// <summary>
    /// Relay mode: get the full text content for a history item.
    /// </summary>
    public Task<GetHistoryTextResponse> GetHistoryText(GetHistoryTextRequest request)
    {
        var roomId = GetJoinedRoomIdOrThrow();
        var itemId = (request?.ItemId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(itemId)) throw new HubException("itemId is required.");

        var text = _history.GetTextPayload(roomId, itemId);
        if (text is null) throw new HubException("history item not found.");
        return Task.FromResult(new GetHistoryTextResponse(itemId, text));
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

        // Converge: send latest relay clipboard value for this room to the caller.
        var latestText = _state.GetLatest(roomId);
        if (latestText is not null)
        {
            await Clients.Caller.SendAsync(ClipboardChangedMethod, latestText);
        }
    }

    /// <summary>
    /// Relay mode: return last N history items for the joined room.
    /// </summary>
    public Task<GetHistoryResponse> GetHistory(GetHistoryRequest request)
    {
        var roomId = GetJoinedRoomIdOrThrow();
        var limit = request?.Limit ?? 10;
        var items = _history.GetHistory(roomId, limit).ToArray();
        return Task.FromResult(new GetHistoryResponse(new HistoryList(roomId, items)));
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

    /// <summary>
    /// Relay mode: publish a file payload into room history (server stores bytes; clients download via /download/{roomId}/{itemId}).
    /// </summary>
    public async Task FilePublish(ClipboardFilePublish publish)
    {
        if (publish is null) throw new HubException("payload is required.");
        if (publish.DeviceId == Guid.Empty) throw new HubException("deviceId is required.");
        if (publish.Bytes is null) throw new HubException("bytes is required.");

        var roomId = GetJoinedRoomIdOrThrow();

        // Hard cap to protect server memory.
        const int serverMax = 10 * 1024 * 1024;
        if (publish.Bytes.Length == 0) throw new HubException("bytes must be non-empty.");
        if (publish.Bytes.Length > serverMax) throw new HubException($"file exceeds max of {serverMax} bytes.");

        var fileName = (publish.FileName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "file.bin";
        var contentType = (publish.ContentType ?? "").Trim();
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var hash = SHA256.HashData(publish.Bytes);
        var id = $"{now}_{publish.DeviceId:N}_{ClipboardProtocol.HashToHex(hash)}";

        // Store payload for HTTP download.
        _files.Put(roomId, id, publish.Bytes);

        var item = new HistoryItem(
            Id: id,
            RoomId: roomId,
            Kind: HistoryItemKind.File,
            OriginDeviceId: publish.DeviceId,
            TsUtcMs: now,
            Title: fileName,
            Preview: null,
            SizeBytes: publish.Bytes.Length,
            ContentHash: hash,
            ContentType: contentType,
            ProviderFileId: null,
            ObjectKey: null);

        _history.Append(roomId, item, maxItems: 10);

        await Clients.Group(roomId).SendAsync(HistoryItemAddedMethod, new HistoryItemAdded(item));
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
