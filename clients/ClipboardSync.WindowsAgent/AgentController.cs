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
    private readonly object _historyGate = new();

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
    private HistoryItem[] _historyCache = Array.Empty<HistoryItem>();

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

            if (_settings.IsDriveMode)
            {
                // Drive mode needs Google OAuth; if server auth is enabled, we also authenticate using the same Google account.
                _ = StartDriveModeAsync();
            }
            else
            {
                _ = _relay.ConnectAsync();
            }

            // Apply persisted preference after boot.
            SetPublishEnabled(_publishEnabledInitial);

            if (_settings.IsDriveMode)
            {
                // Drive sync is started by StartDriveModeAsync (after Google initialization).
            }
        }
    }

    private async Task StartDriveModeAsync()
    {
        try
        {
            var svc = await EnsureDriveServiceAsync();
            _log.Info($"Google Drive auth ready. Using OAuth client secrets: {svc.ClientSecretsPath}");

            if (_settings.UseGoogleAccountAuth)
            {
                // Ensure we have a fresh Google access token, then exchange for a RelayServer JWT.
                var googleAccessToken = await svc.Credential.GetAccessTokenForRequestAsync();
                try
                {
                    var status = await Auth.GoogleServerAuth.GetServerAuthStatusAsync(_settings.ServerBaseUrl, CancellationToken.None);
                    if (!status.Enabled)
                    {
                        _log.Warn(
                            "Google account auth is enabled, but RelayServer auth is DISABLED. " +
                            "Enable server auth (Auth:Enabled=true, Auth:JwtSigningKey, Auth:GoogleClientIds) or turn OFF 'Use Google account for authentication'.");
                        throw new InvalidOperationException("RelayServer auth disabled.");
                    }

                    // Prefer ID token when available (more reliable audience validation than access token tokeninfo).
                    var googleIdToken = svc.Credential.Token?.IdToken;
                    if (!string.IsNullOrWhiteSpace(googleIdToken))
                        _log.Info("Using Google ID token for RelayServer auth exchange.");
                    else
                        _log.Warn("Google ID token not available; falling back to Google access token for RelayServer auth exchange.");

                    var auth = await Auth.GoogleServerAuth.LoginAsync(_settings.ServerBaseUrl, googleIdToken, googleAccessToken, CancellationToken.None);
                    _log.Info($"Authenticated to RelayServer via Google. subject={auth.Subject} expiresUtc={auth.ExpiresUtc:O}");
                    _relay?.SetBearerToken(auth.Token);
                }
                catch (Exception ex)
                {
                    _log.Error(
                        "Google account auth is enabled, but RelayServer token exchange failed. " +
                        "If you want Room-based testing, turn OFF 'Use Google account for authentication'. " +
                        "If you want Google-based sharing, ensure you're running the latest RelayServer from THIS repo and that it exposes /auth/status and /auth/google. " +
                        "Then enable auth on the server (Auth:Enabled=true, Auth:JwtSigningKey, Auth:GoogleClientIds).",
                        ex);
                    throw;
                }
            }

            await (_relay?.ConnectAsync() ?? Task.CompletedTask);

            lock (_gate)
            {
                if (_driveSync is null && _relay is not null && _poller is not null)
                {
                    _driveSync = new Drive.DriveClipboardSync(
                        _settings,
                        _relay,
                        _poller,
                        _loopGuard,
                        _log,
                        store: svc.Store,
                        manifestStore: svc.Manifest);
                }
            }

            _driveSync?.Start();
        }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException ioe &&
                ioe.Message.Contains("GoogleClientSecretsPath is required", StringComparison.OrdinalIgnoreCase))
            {
                _log.Error("Drive mode startup failed: GoogleClientSecretsPath is missing. Set 'Google secrets path' in tray Settings, then Restart.", ex);
                return;
            }

            _log.Error("Drive mode startup failed", ex);
        }
    }

    public Task ConnectAsync() => _relay?.ConnectAsync() ?? Task.CompletedTask;
    public Task DisconnectAsync() => _relay?.DisconnectAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Returns the last known history items (best-effort, in-memory cache).
    /// Useful for making UI feel responsive in Drive mode, where fetching the manifest can be slow.
    /// </summary>
    public HistoryItem[] GetHistoryCacheSnapshot(int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        lock (_historyGate)
        {
            if (_historyCache.Length == 0) return Array.Empty<HistoryItem>();
            if (_historyCache.Length <= limit) return _historyCache.ToArray();
            return _historyCache.Take(limit).ToArray();
        }
    }

    public async Task<HistoryItem[]> GetRemoteHistoryAsync(int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        try
        {
            if (_settings.IsDriveMode)
            {
                var (manifest, _) = await EnsureDriveHistoryDepsAsync();
                var loaded = await manifest.LoadAsync(_settings.RoomId, CancellationToken.None);
                var items = loaded.ToHistoryItems().Take(limit).ToArray();
                lock (_historyGate) _historyCache = items.ToArray();
                return items;
            }

            var relayItems = await (_relay?.GetHistoryAsync(limit) ?? Task.FromResult(Array.Empty<HistoryItem>()));
            lock (_historyGate) _historyCache = relayItems.ToArray();
            return relayItems;
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
                var roomId = EffectiveRoomId(_settings);
                var objectKey = $"clips/{roomId}/{now.ToUnixTimeMilliseconds()}_{_settings.DeviceId:N}_{ClipboardProtocol.HashToHex(hash)}_{fileName}";

                await using var ms = new MemoryStream(bytes);
                var (fileId, sizeBytes) = await svc.Store.UploadFileAsync(objectKey, fileName, ms, "application/octet-stream", CancellationToken.None);

                var pointer = new ClipboardItemPointer(
                    RoomId: roomId,
                    OriginDeviceId: _settings.DeviceId,
                    TsUtcMs: now.ToUnixTimeMilliseconds(),
                    ObjectKey: objectKey,
                    ProviderFileId: fileId,
                    ContentHash: hash,
                    SizeBytes: sizeBytes == 0 ? bytes.Length : sizeBytes,
                    ContentType: "file");

                await (_relay?.PublishPointerAsync(new ClipboardPointerPublish(pointer)) ?? Task.CompletedTask);

                // Update manifest (best effort).
                var manifest = await svc.Manifest.LoadAsync(roomId, CancellationToken.None);
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

    internal static string EffectiveRoomId(AppSettingsSnapshot settings)
        => settings.UseGoogleAccountAuth ? "default" : settings.RoomId;

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
    public DriveServiceCache(IDriveManifestStore manifest, IDriveClipboardStore store, Google.Apis.Drive.v3.DriveService drive, Google.Apis.Auth.OAuth2.UserCredential credential, string clientSecretsPath)
    {
        Manifest = manifest;
        Store = store;
        Drive = drive;
        Credential = credential;
        ClientSecretsPath = clientSecretsPath;
    }

    public IDriveManifestStore Manifest { get; }
    public IDriveClipboardStore Store { get; }
    public Google.Apis.Drive.v3.DriveService Drive { get; }
    public Google.Apis.Auth.OAuth2.UserCredential Credential { get; }
    public string ClientSecretsPath { get; }
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

        var tokenScope = _settings.UseGoogleAccountAuth ? "google" : _settings.RoomId;
        var tokenDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipboardSync",
            "googleTokens",
            tokenScope);

        // In Google-auth mode, we intentionally ignore any user-provided secrets path override.
        // The app will use a bundled client_secret*.json next to the executable.
        var secretsPath = _settings.UseGoogleAccountAuth ? "" : _settings.GoogleClientSecretsPath;
        var (drive, credential, resolvedPath) = await GoogleDriveAuth.GetDriveServiceAndCredentialAsync(secretsPath, tokenDir, CancellationToken.None);
        _driveSvc = new DriveServiceCache(new DriveManifestStore(drive), new DriveClipboardStore(drive), drive, credential, resolvedPath);
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
    bool UseGoogleAccountAuth,
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
            s.UseGoogleAccountAuth,
            s.MaxInlineTextBytes,
            s.MaxUploadBytes) { }

    public bool IsDriveMode => string.Equals(SyncMode, "Drive", StringComparison.OrdinalIgnoreCase);
}


