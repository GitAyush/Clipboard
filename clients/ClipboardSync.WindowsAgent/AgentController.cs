using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent.Clipboard;
using ClipboardSync.WindowsAgent.Drive;
using ClipboardSync.WindowsAgent.Sync;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed partial class AgentController : IDisposable
{
    private readonly object _gate = new();

    private readonly ClipboardLoopGuard _loopGuard = new();
    private readonly AppSettingsSnapshot _settings;
    private readonly bool _publishEnabledInitial;
    private readonly LogBuffer _log = new();
    private int _publishEnabled; // 1=true, 0=false

    private RelayConnection? _relay;
    private WindowsClipboardPoller? _poller;
    private Drive.DriveClipboardSync? _driveSync;

    // History helpers (Drive mode: separate lightweight access for manifest + downloads)
    private DriveServiceCache? _driveSvc;

    public event Action<string>? StatusChanged;
    public event Action? HistoryMayHaveChanged;

    public AgentController(Settings.AppSettings settings)
    {
        _settings = new AppSettingsSnapshot(settings);
        _publishEnabledInitial = settings.PublishLocalClipboard;
        _publishEnabled = _publishEnabledInitial ? 1 : 0;
    }

    public LogBuffer Log => _log;
    public bool PublishEnabled => Volatile.Read(ref _publishEnabled) == 1;

    public void SetPublishEnabled(bool enabled)
    {
        Volatile.Write(ref _publishEnabled, enabled ? 1 : 0);
        _log.Info($"Publish local clipboard: {(enabled ? "ON" : "OFF")}");
    }

    public void Start()
    {
        lock (_gate)
        {
            _relay ??= new RelayConnection(_settings, _log);
            _poller ??= new WindowsClipboardPoller(_settings, _loopGuard, _log);

            _relay.ClipboardChanged += OnRemoteClipboardChanged;
            _relay.ClipboardPointerChanged += OnRemotePointerChanged;
            _relay.HistoryItemAdded += OnHistoryItemAdded;
            _relay.StatusChanged += OnRelayStatusChanged;
            _poller.TextChanged += OnLocalClipboardTextChanged;
            _poller.FilesChanged += OnLocalClipboardFilesChanged;

            _poller.Start();
            _ = _relay.ConnectAsync();

            // Apply persisted preference after boot.
            SetPublishEnabled(_publishEnabledInitial);

            if (_settings.IsDriveMode)
            {
                _driveSync ??= new Drive.DriveClipboardSync(_settings, _relay, _poller, _loopGuard, _log);
                _driveSync.Start();
            }
        }
    }

    public Task ConnectAsync() => _relay?.ConnectAsync() ?? Task.CompletedTask;
    public Task DisconnectAsync() => _relay?.DisconnectAsync() ?? Task.CompletedTask;

    public async Task<HistoryItem[]> GetRemoteHistoryAsync(int limit)
    {
        try
        {
            if (_settings.IsDriveMode)
            {
                var (manifest, _) = await EnsureDriveHistoryDepsAsync();
                var loaded = await manifest.LoadAsync(_settings.RoomId, CancellationToken.None);
                return loaded.ToHistoryItems().Take(limit).ToArray();
            }

            return await (_relay?.GetHistoryAsync(limit) ?? Task.FromResult(Array.Empty<HistoryItem>()));
        }
        catch (Exception ex)
        {
            _log.Error("GetRemoteHistory failed", ex);
            return Array.Empty<HistoryItem>();
        }
    }

    public async Task CopyHistoryItemToClipboardAsync(HistoryItem item)
    {
        if (item.Kind != HistoryItemKind.Text) return;

        try
        {
            string? text;
            if (_settings.IsDriveMode)
            {
                if (string.IsNullOrWhiteSpace(item.ProviderFileId)) return;
                var (_, store) = await EnsureDriveHistoryDepsAsync();
                text = await store.DownloadTextAsync(item.ProviderFileId!, CancellationToken.None);
            }
            else
            {
                text = await (_relay?.GetHistoryTextAsync(item.Id) ?? Task.FromResult<string?>(null));
            }

            if (text is null) return;
            _poller?.ApplyRemoteText(text);
        }
        catch (Exception ex)
        {
            _log.Error("CopyHistoryItemToClipboard failed", ex);
        }
    }

    public async Task DownloadHistoryFileAsync(HistoryItem item, string targetPath)
    {
        if (item.Kind != HistoryItemKind.File) return;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
            if (_settings.IsDriveMode)
            {
                if (string.IsNullOrWhiteSpace(item.ProviderFileId)) return;
                var svc = await EnsureDriveServiceAsync();
                await svc.Store.DownloadFileToPathAsync(item.ProviderFileId!, targetPath, CancellationToken.None);
                return;
            }

            // Relay: download via HTTP endpoint.
            var baseUrl = (_settings.ServerBaseUrl ?? "").TrimEnd('/');
            var url = $"{baseUrl}/download/{Uri.EscapeDataString(_settings.RoomId)}/{Uri.EscapeDataString(item.Id)}";
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllBytesAsync(targetPath, bytes);
        }
        catch (Exception ex)
        {
            _log.Error("DownloadHistoryFile failed", ex);
        }
    }

    public async Task UploadFileAsync(string filePath)
    {
        try
        {
            if (!PublishEnabled) return;
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (!File.Exists(filePath)) return;

            var fi = new FileInfo(filePath);
            var size = fi.Length;
            if (size <= 0) return;
            if (size > _settings.MaxUploadBytes)
            {
                _log.Warn($"File exceeds MaxUploadBytes={_settings.MaxUploadBytes} (bytes={size}); refusing upload.");
                return;
            }

            var fileName = fi.Name;
            var bytes = await File.ReadAllBytesAsync(filePath);
            var hash = SHA256.HashData(bytes);

            if (_settings.IsDriveMode)
            {
                var svc = await EnsureDriveServiceAsync();
                var now = DateTimeOffset.UtcNow;
                var objectKey = $"clips/{_settings.RoomId}/{now.ToUnixTimeMilliseconds()}_{_settings.DeviceId:N}_{ClipboardProtocol.HashToHex(hash)}_{fileName}";

                await using var ms = new MemoryStream(bytes);
                var (fileId, sizeBytes) = await svc.Store.UploadFileAsync(objectKey, fileName, ms, "application/octet-stream", CancellationToken.None);

                var pointer = new ClipboardItemPointer(
                    RoomId: _settings.RoomId,
                    OriginDeviceId: _settings.DeviceId,
                    TsUtcMs: now.ToUnixTimeMilliseconds(),
                    ObjectKey: objectKey,
                    ProviderFileId: fileId,
                    ContentHash: hash,
                    SizeBytes: sizeBytes == 0 ? bytes.Length : sizeBytes,
                    ContentType: "file");

                await (_relay?.PublishPointerAsync(new ClipboardPointerPublish(pointer)) ?? Task.CompletedTask);

                // Update manifest (best effort).
                var manifest = await svc.Manifest.LoadAsync(_settings.RoomId, CancellationToken.None);
                manifest.AppendOrReplaceNewestFirst(
                    DriveHistoryManifestItem.FromPointer(pointer, HistoryItemKind.File, title: fileName, preview: null),
                    maxItems: 10);
                await svc.Manifest.SaveAsync(manifest, CancellationToken.None);

                _log.Info($"File uploaded to Drive and pointer published. name={fileName} size={bytes.Length} fileId={fileId}");
                HistoryMayHaveChanged?.Invoke();
                return;
            }

            await (_relay?.PublishFileAsync(new ClipboardFilePublish(_settings.DeviceId, fileName, "application/octet-stream", bytes)) ?? Task.CompletedTask);
            _log.Info($"File uploaded to relay history. name={fileName} size={bytes.Length}");
            HistoryMayHaveChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error("UploadFile failed", ex);
        }
    }

    private void OnHistoryItemAdded(HistoryItemAdded added)
    {
        // Relay mode: server confirms history append (metadata only).
        HistoryMayHaveChanged?.Invoke();
    }

    private async void OnLocalClipboardTextChanged(object? sender, string text)
    {
        try
        {
            if (!PublishEnabled)
                return;

            var textBytes = System.Text.Encoding.UTF8.GetByteCount(text);
            if (textBytes > _settings.MaxInlineTextBytes)
            {
                _log.Warn($"Local text exceeds MaxInlineTextBytes={_settings.MaxInlineTextBytes} (bytes={textBytes}); skipping inline publish.");
                return;
            }

            if (_settings.IsDriveMode)
            {
                await (_driveSync?.OnLocalTextChangedAsync(text) ?? Task.CompletedTask);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var hash = ClipboardProtocol.ComputeTextHashUtf8(text);

            if (_loopGuard.ShouldSuppressLocalPublish(now, hash))
                return;

            var publish = new ClipboardPublish(
                DeviceId: _settings.DeviceId,
                ClientItemId: Guid.NewGuid(),
                TsClientUtcMs: now.ToUnixTimeMilliseconds(),
                Text: text,
                TextHash: hash);

            await (_relay?.PublishAsync(publish) ?? Task.CompletedTask);
            HistoryMayHaveChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error("Local publish failed", ex);
        }
    }

    private async void OnLocalClipboardFilesChanged(object? sender, IReadOnlyList<string> files)
    {
        try
        {
            if (!PublishEnabled) return;
            if (files is null || files.Count == 0) return;

            // MVP: auto-handle a single small file only.
            if (files.Count != 1) return;
            var path = files[0];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var size = new FileInfo(path).Length;
            const long autoThreshold = 100 * 1024;
            if (size > autoThreshold)
            {
                _log.Info($"File copied (bytes={size}). Above auto threshold; use tray 'Upload file...'.");
                return;
            }

            await UploadFileAsync(path);
        }
        catch (Exception ex)
        {
            _log.Error("Local file publish failed", ex);
        }
    }

    private void OnRemoteClipboardChanged(ClipboardChanged changed)
    {
        try
        {
            if (_settings.IsDriveMode)
                return; // Drive mode ignores payload events.

            if (ClipboardLoopGuard.ShouldIgnoreRemote(_settings.DeviceId, changed.OriginDeviceId))
            {
                _log.Info("Received remote event from self; ignoring.");
                return;
            }

            _log.Info($"Remote event received; applying to clipboard. origin={changed.OriginDeviceId}");
            _poller?.ApplyRemoteText(changed.Text);
        }
        catch (Exception ex)
        {
            _log.Error("Remote apply failed", ex);
        }
    }

    private void OnRemotePointerChanged(ClipboardPointerChanged changed)
    {
        if (!_settings.IsDriveMode) return;
        _ = HandlePointerAndNotifyAsync(changed.Pointer);
    }

    private async Task HandlePointerAndNotifyAsync(ClipboardItemPointer pointer)
    {
        await (_driveSync?.OnPointerChangedAsync(pointer) ?? Task.CompletedTask);
        // Drive mode: pointer receipt implies history likely changed (manifest updated by origin).
        HistoryMayHaveChanged?.Invoke();
    }

    private void OnRelayStatusChanged(string status)
    {
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_relay is not null)
            {
                _relay.ClipboardChanged -= OnRemoteClipboardChanged;
                _relay.ClipboardPointerChanged -= OnRemotePointerChanged;
                _relay.HistoryItemAdded -= OnHistoryItemAdded;
                _relay.StatusChanged -= OnRelayStatusChanged;
                _relay.Dispose();
                _relay = null;
            }

            if (_poller is not null)
            {
                _poller.TextChanged -= OnLocalClipboardTextChanged;
                _poller.FilesChanged -= OnLocalClipboardFilesChanged;
                _poller.Dispose();
                _poller = null;
            }

            _driveSync?.Dispose();
            _driveSync = null;
        }
    }
}

internal sealed class DriveServiceCache
{
    public DriveServiceCache(IDriveManifestStore manifest, IDriveClipboardStore store, Google.Apis.Drive.v3.DriveService drive)
    {
        Manifest = manifest;
        Store = store;
        Drive = drive;
    }

    public IDriveManifestStore Manifest { get; }
    public IDriveClipboardStore Store { get; }
    public Google.Apis.Drive.v3.DriveService Drive { get; }
}

partial class AgentController
{
    private async Task<(IDriveManifestStore manifest, IDriveClipboardStore store)> EnsureDriveHistoryDepsAsync()
    {
        var svc = await EnsureDriveServiceAsync();
        return (svc.Manifest, svc.Store);
    }

    private async Task<DriveServiceCache> EnsureDriveServiceAsync()
    {
        if (_driveSvc is not null) return _driveSvc;

        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipboardSync",
            "googleTokens",
            _settings.RoomId);

        var drive = await GoogleDriveAuth.GetDriveServiceAsync(_settings.GoogleClientSecretsPath, tokenDir, CancellationToken.None);
        _driveSvc = new DriveServiceCache(new DriveManifestStore(drive), new DriveClipboardStore(drive), drive);
        return _driveSvc;
    }
}

public readonly record struct AppSettingsSnapshot(
    Guid DeviceId,
    string DeviceName,
    string ServerBaseUrl,
    string SyncMode,
    string RoomId,
    string RoomSecret,
    string GoogleClientSecretsPath,
    int MaxInlineTextBytes,
    int MaxUploadBytes)
{
    public AppSettingsSnapshot(Settings.AppSettings s)
        : this(
            s.DeviceId,
            s.DeviceName,
            s.ServerBaseUrl,
            s.SyncMode ?? "Relay",
            s.RoomId ?? "default",
            s.RoomSecret ?? "",
            s.GoogleClientSecretsPath ?? "",
            s.MaxInlineTextBytes,
            s.MaxUploadBytes) { }

    public bool IsDriveMode => string.Equals(SyncMode, "Drive", StringComparison.OrdinalIgnoreCase);
}


