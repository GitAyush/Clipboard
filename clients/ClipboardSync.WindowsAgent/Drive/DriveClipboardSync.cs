using System.IO;
using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent.Drive;

/// <summary>
/// Drive-backed sync engine (server sees pointers only).
/// - On local clipboard change: upload text to Drive, then publish pointer over SignalR
/// - On pointer received: download from Drive, then apply to clipboard
/// </summary>
public sealed class DriveClipboardSync : IDisposable
{
    private readonly AppSettingsSnapshot _settings;
    private readonly IRelayPointerTransport _relay;
    private readonly IClipboardApplier _clipboard;
    private readonly ClipboardLoopGuard _loopGuard;
    private readonly LogBuffer _log;

    private IDriveClipboardStore? _store;
    private int _joinedRoom;

    public DriveClipboardSync(
        AppSettingsSnapshot settings,
        IRelayPointerTransport relay,
        IClipboardApplier clipboard,
        ClipboardLoopGuard loopGuard,
        LogBuffer log)
    {
        _settings = settings;
        _relay = relay;
        _clipboard = clipboard;
        _loopGuard = loopGuard;
        _log = log;
    }

    /// <summary>
    /// Test-friendly constructor: provide a pre-configured store (no interactive OAuth).
    /// </summary>
    public DriveClipboardSync(
        AppSettingsSnapshot settings,
        IRelayPointerTransport relay,
        IClipboardApplier clipboard,
        ClipboardLoopGuard loopGuard,
        LogBuffer log,
        IDriveClipboardStore store)
        : this(settings, relay, clipboard, loopGuard, log)
    {
        _store = store;
    }

    public void Start()
    {
        _log.Info("Drive mode enabled (server will relay pointers only).");
        if (_store is null)
        {
            _ = EnsureInitializedAsync();
        }
        else
        {
            _ = EnsureJoinedRoomAsync();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        try
        {
            var tokenDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClipboardSync",
                "googleTokens",
                _settings.RoomId);

            var drive = await GoogleDriveAuth.GetDriveServiceAsync(_settings.GoogleClientSecretsPath, tokenDir, CancellationToken.None);
            _store = new DriveClipboardStore(drive);

            await EnsureJoinedRoomAsync();

            _log.Info("Drive mode initialized (Google auth OK, room joined).");
        }
        catch (Exception ex)
        {
            _log.Error("Drive mode initialization failed", ex);
        }
    }

    private async Task EnsureJoinedRoomAsync()
    {
        if (Volatile.Read(ref _joinedRoom) == 1) return;
        if (string.IsNullOrWhiteSpace(_settings.RoomId) || string.IsNullOrWhiteSpace(_settings.RoomSecret))
            throw new InvalidOperationException("RoomId and RoomSecret are required for Drive mode.");

        await _relay.JoinRoomAsync(_settings.RoomId, _settings.RoomSecret);
        Volatile.Write(ref _joinedRoom, 1);
    }

    public async Task OnLocalTextChangedAsync(string text)
    {
        if (_store is null) return;

        await EnsureJoinedRoomAsync();

        var now = DateTimeOffset.UtcNow;
        var hash = ClipboardProtocol.ComputeTextHashUtf8(text);

        // Reuse existing guard: debounce + duplicates.
        if (_loopGuard.ShouldSuppressLocalPublish(now, hash))
            return;

        var objectKey = BuildObjectKey(_settings.RoomId, now, _settings.DeviceId, hash);
        _log.Info($"Uploading to Drive... key={objectKey}");

        var (fileId, sizeBytes) = await _store.UploadTextAsync(objectKey, text, CancellationToken.None);

        var pointer = new ClipboardItemPointer(
            RoomId: _settings.RoomId,
            OriginDeviceId: _settings.DeviceId,
            TsUtcMs: now.ToUnixTimeMilliseconds(),
            ObjectKey: objectKey,
            ProviderFileId: fileId,
            ContentHash: hash,
            SizeBytes: sizeBytes,
            ContentType: "text");

        await _relay.PublishPointerAsync(new ClipboardPointerPublish(pointer));
        _log.Info($"Pointer published. fileId={fileId} size={sizeBytes}");
    }

    public async Task OnPointerChangedAsync(ClipboardItemPointer pointer)
    {
        try
        {
            if (_store is null) return;

            if (ClipboardLoopGuard.ShouldIgnoreRemote(_settings.DeviceId, pointer.OriginDeviceId))
            {
                _log.Info("Pointer from self; ignoring.");
                return;
            }

            if (!string.Equals(pointer.RoomId, _settings.RoomId, StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(pointer.ProviderFileId))
            {
                _log.Warn("Pointer missing ProviderFileId; cannot download.");
                return;
            }

            _log.Info($"Downloading from Drive... fileId={pointer.ProviderFileId}");
            var text = await _store.DownloadTextAsync(pointer.ProviderFileId, CancellationToken.None);
            _clipboard.ApplyRemoteText(text);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to apply pointer", ex);
        }
    }

    private static string BuildObjectKey(string roomId, DateTimeOffset nowUtc, Guid deviceId, byte[] hash)
        => $"clips/{roomId}/{nowUtc.ToUnixTimeMilliseconds()}_{deviceId:N}_{ClipboardProtocol.HashToHex(hash)}.txt";

    public void Dispose()
    {
        // DriveService doesn't need disposal; leave for GC.
    }
}


