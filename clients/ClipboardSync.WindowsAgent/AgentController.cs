using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent.Clipboard;
using ClipboardSync.WindowsAgent.Sync;
using System.Diagnostics.CodeAnalysis;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed class AgentController : IDisposable
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

    public event Action<string>? StatusChanged;

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
            _relay.StatusChanged += OnRelayStatusChanged;
            _poller.TextChanged += OnLocalClipboardTextChanged;

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

    private async void OnLocalClipboardTextChanged(object? sender, string text)
    {
        try
        {
            if (!PublishEnabled)
                return;

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
        }
        catch (Exception ex)
        {
            _log.Error("Local publish failed", ex);
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
        _ = _driveSync?.OnPointerChangedAsync(changed.Pointer);
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
                _relay.StatusChanged -= OnRelayStatusChanged;
                _relay.Dispose();
                _relay = null;
            }

            if (_poller is not null)
            {
                _poller.TextChanged -= OnLocalClipboardTextChanged;
                _poller.Dispose();
                _poller = null;
            }

            _driveSync?.Dispose();
            _driveSync = null;
        }
    }
}

public readonly record struct AppSettingsSnapshot(
    Guid DeviceId,
    string DeviceName,
    string ServerBaseUrl,
    string SyncMode,
    string RoomId,
    string RoomSecret,
    string GoogleClientSecretsPath)
{
    public AppSettingsSnapshot(Settings.AppSettings s)
        : this(
            s.DeviceId,
            s.DeviceName,
            s.ServerBaseUrl,
            s.SyncMode ?? "Relay",
            s.RoomId ?? "default",
            s.RoomSecret ?? "",
            s.GoogleClientSecretsPath ?? "") { }

    public bool IsDriveMode => string.Equals(SyncMode, "Drive", StringComparison.OrdinalIgnoreCase);
}


