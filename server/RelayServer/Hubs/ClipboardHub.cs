using System.Text;
using System.Security.Cryptography;
using System.Security.Claims;
using ClipboardSync.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RelayServer.Auth;
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
    private const string JoinedRoomIdItemKey = "roomId";   // logical (client-facing) room id
    private const string JoinedRoomKeyItemKey = "roomKey"; // internal group/state key (may include user scoping)

    private readonly InMemoryClipboardState _state;
    private readonly InMemoryRoomRegistry _rooms;
    private readonly InMemoryPointerState _pointers;
    private readonly InMemoryHistoryState _history;
    private readonly InMemoryFilePayloadStore _files;
    private readonly ILogger<ClipboardHub> _logger;
    private readonly AuthOptions _auth;

    public ClipboardHub(
        InMemoryClipboardState state,
        InMemoryRoomRegistry rooms,
        InMemoryPointerState pointers,
        InMemoryHistoryState history,
        InMemoryFilePayloadStore files,
        ILogger<ClipboardHub> logger,
        IOptions<AuthOptions> auth)
    {
        _state = state;
        _rooms = rooms;
        _pointers = pointers;
        _history = history;
        _files = files;
        _logger = logger;
        _auth = auth.Value ?? new AuthOptions();
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
        var (roomId, roomKey) = GetJoinedRoomOrThrow();
        var computedHash = ValidateAndComputeHash(publish);

        var changed = new ClipboardChanged(
            OriginDeviceId: publish.DeviceId,
            ServerTsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text: publish.Text,
            TextHash: computedHash);

        _state.SetLatest(roomKey, changed);

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

        _history.Append(roomKey, item, maxItems: 10);
        _history.PutTextPayload(roomKey, id, publish.Text);

        // Broadcast within room only.
        await Clients.Group(roomKey).SendAsync(ClipboardChangedMethod, changed);
        await Clients.Group(roomKey).SendAsync(HistoryItemAddedMethod, new HistoryItemAdded(item));
    }

    /// <summary>
    /// Relay mode: get the full text content for a history item.
    /// </summary>
    public Task<GetHistoryTextResponse> GetHistoryText(GetHistoryTextRequest request)
    {
        var (_, roomKey) = GetJoinedRoomOrThrow();
        var itemId = (request?.ItemId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(itemId)) throw new HubException("itemId is required.");

        var text = _history.GetTextPayload(roomKey, itemId);
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

        if (_auth.Enabled)
        {
            EnsureAuthenticated();
            if (string.IsNullOrWhiteSpace(roomId)) roomId = "default";

            // Scope by authenticated Google subject so only the same Google account shares clipboard.
            // We still keep "roomId" for testing/segmentation within an account.
            var sub = Context.User?.FindFirstValue(JwtIssuer.ClaimSub) ?? "";
            if (string.IsNullOrWhiteSpace(sub)) throw new HubException("missing subject.");
            var roomKey = $"{sub}|{roomId}";

            Context.Items[JoinedRoomIdItemKey] = roomId;
            Context.Items[JoinedRoomKeyItemKey] = roomKey;
            await Groups.AddToGroupAsync(Context.ConnectionId, roomKey);

            _logger.LogInformation("Client joined auth-scoped room. connectionId={ConnectionId} roomId={RoomId}", Context.ConnectionId, roomId);

            // Converge: send latest pointer for this room to the caller.
            var latestPointer = _pointers.GetLatest(roomKey);
            if (latestPointer is not null)
            {
                await Clients.Caller.SendAsync(ClipboardPointerChangedMethod, new ClipboardPointerChanged(latestPointer));
            }

            // Converge: send latest relay clipboard value for this room to the caller.
            var latestRelayText = _state.GetLatest(roomKey);
            if (latestRelayText is not null)
            {
                await Clients.Caller.SendAsync(ClipboardChangedMethod, latestRelayText);
            }

            return;
        }

        if (!_rooms.EnsureRoomAndValidateSecret(roomId, roomSecret))
            throw new HubException("invalid roomId/roomSecret.");

        Context.Items[JoinedRoomIdItemKey] = roomId;
        Context.Items[JoinedRoomKeyItemKey] = roomId;
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
        var (roomId, roomKey) = GetJoinedRoomOrThrow();
        var limit = request?.Limit ?? 10;
        var items = _history.GetHistory(roomKey, limit).ToArray();
        return Task.FromResult(new GetHistoryResponse(new HistoryList(roomId, items)));
    }

    /// <summary>
    /// Phase 2 (pointer-only): publish a pointer to a clipboard item stored outside this server.
    /// No clipboard payload should be sent here.
    /// </summary>
    public async Task ClipboardPointerPublish(ClipboardPointerPublish publish)
    {
        if (publish?.Pointer is null) throw new HubException("pointer is required.");

        var (roomId, roomKey) = GetJoinedRoomOrThrow();
        var pointer = publish.Pointer;
        if (!string.Equals(roomId, pointer.RoomId, StringComparison.Ordinal))
        {
            // In Google-auth mode, the room is effectively derived from the authenticated user and join context.
            // Older clients (or certain flows) may still send the configured RoomId; treat it as advisory and
            // normalize to the joined room to avoid hard failures.
            if (_auth.Enabled)
            {
                _logger.LogWarning(
                    "Pointer roomId mismatch in auth mode; normalizing. joinedRoomId={JoinedRoomId} pointerRoomId={PointerRoomId} connectionId={ConnectionId}",
                    roomId,
                    pointer.RoomId,
                    Context.ConnectionId);
                pointer = pointer with { RoomId = roomId };
            }
            else
            {
                throw new HubException("pointer roomId does not match joined room.");
            }
        }

        // Basic validation (metadata only)
        if (pointer.OriginDeviceId == Guid.Empty) throw new HubException("originDeviceId is required.");
        if (pointer.TsUtcMs <= 0) throw new HubException("tsUtcMs is required.");
        if (string.IsNullOrWhiteSpace(pointer.ObjectKey)) throw new HubException("objectKey is required.");
        if (pointer.ContentHash is null || pointer.ContentHash.Length != 32) throw new HubException("contentHash must be 32 bytes.");
        if (pointer.SizeBytes < 0) throw new HubException("sizeBytes must be >= 0.");
        if (string.IsNullOrWhiteSpace(pointer.ContentType)) throw new HubException("contentType is required.");

        if (_auth.Enabled)
            _pointers.SetLatest(roomKey, pointer);
        else
            _pointers.SetLatest(pointer);

        // Broadcast within room only.
        await Clients.Group(roomKey).SendAsync(ClipboardPointerChangedMethod, new ClipboardPointerChanged(pointer));
    }

    /// <summary>
    /// Relay mode: publish a file payload into room history (server stores bytes; clients download via /download/{roomId}/{itemId}).
    /// </summary>
    public async Task FilePublish(ClipboardFilePublish publish)
    {
        if (publish is null) throw new HubException("payload is required.");
        if (publish.DeviceId == Guid.Empty) throw new HubException("deviceId is required.");
        if (publish.Bytes is null) throw new HubException("bytes is required.");

        var (roomId, roomKey) = GetJoinedRoomOrThrow();

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
        _files.Put(roomKey, id, publish.Bytes);

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

        _history.Append(roomKey, item, maxItems: 10);

        await Clients.Group(roomKey).SendAsync(HistoryItemAddedMethod, new HistoryItemAdded(item));
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

    private (string roomId, string roomKey) GetJoinedRoomOrThrow()
    {
        if (_auth.Enabled) EnsureAuthenticated();

        if (!Context.Items.TryGetValue(JoinedRoomIdItemKey, out var rid) ||
            !Context.Items.TryGetValue(JoinedRoomKeyItemKey, out var rkey))
            throw new HubException("must JoinRoom first.");

        var roomId = rid as string;
        var roomKey = rkey as string;
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(roomKey))
            throw new HubException("must JoinRoom first.");

        return (roomId, roomKey);
    }

    private void EnsureAuthenticated()
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
            throw new HubException("auth required.");
    }
}
