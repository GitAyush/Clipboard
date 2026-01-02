using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent.Clipboard;
using ClipboardSync.WindowsAgent.Sync;

namespace ClipboardSync.WindowsAgent;

public sealed class AgentController : IDisposable
{
    private readonly object _gate = new();

    private readonly ClipboardLoopGuard _loopGuard = new();
    private readonly AppSettingsSnapshot _settings;
    private readonly LogBuffer _log = new();

    private RelayConnection? _relay;
    private WindowsClipboardPoller? _poller;

    public AgentController(Settings.AppSettings settings)
    {
        _settings = new AppSettingsSnapshot(settings);
    }

    public LogBuffer Log => _log;

    public void Start()
    {
        lock (_gate)
        {
            _relay ??= new RelayConnection(_settings, _log);
            _poller ??= new WindowsClipboardPoller(_settings, _loopGuard, _log);

            _relay.ClipboardChanged += OnRemoteClipboardChanged;
            _poller.TextChanged += OnLocalClipboardTextChanged;

            _poller.Start();
            _ = _relay.ConnectAsync();
        }
    }

    public Task ConnectAsync() => _relay?.ConnectAsync() ?? Task.CompletedTask;
    public Task DisconnectAsync() => _relay?.DisconnectAsync() ?? Task.CompletedTask;

    private async void OnLocalClipboardTextChanged(object? sender, string text)
    {
        try
        {
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
            if (ClipboardLoopGuard.ShouldIgnoreRemote(_settings.DeviceId, changed.OriginDeviceId))
                return;

            _poller?.ApplyRemoteText(changed.Text);
        }
        catch (Exception ex)
        {
            _log.Error("Remote apply failed", ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_relay is not null)
            {
                _relay.ClipboardChanged -= OnRemoteClipboardChanged;
                _relay.Dispose();
                _relay = null;
            }

            if (_poller is not null)
            {
                _poller.TextChanged -= OnLocalClipboardTextChanged;
                _poller.Dispose();
                _poller = null;
            }
        }
    }
}

public readonly record struct AppSettingsSnapshot(Guid DeviceId, string DeviceName, string ServerBaseUrl)
{
    public AppSettingsSnapshot(Settings.AppSettings s)
        : this(s.DeviceId, s.DeviceName, s.ServerBaseUrl) { }
}


