using System.Windows;
using System.Windows.Threading;
using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent.Clipboard;

public sealed class WindowsClipboardPoller : IDisposable
{
    private readonly AppSettingsSnapshot _settings;
    private readonly ClipboardLoopGuard _loopGuard;
    private readonly LogBuffer _log;

    private readonly DispatcherTimer _timer;
    private string? _lastSeenText;

    public event EventHandler<string>? TextChanged;

    public WindowsClipboardPoller(AppSettingsSnapshot settings, ClipboardLoopGuard loopGuard, LogBuffer log)
    {
        _settings = settings;
        _loopGuard = loopGuard;
        _log = log;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += (_, _) => PollOnce();
    }

    public void Start()
    {
        _timer.Start();
        _log.Info($"Clipboard poller started. deviceId={_settings.DeviceId}");
    }

    public void ApplyRemoteText(string text)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var now = DateTimeOffset.UtcNow;
            _loopGuard.BeginRemoteApply(now);

            try
            {
                var current = TryGetClipboardText();
                if (current is not null && string.Equals(current, text, StringComparison.Ordinal))
                    return;

                Clipboard.SetText(text);
                _lastSeenText = text;
                _log.Info("Applied remote clipboard text.");
            }
            catch (Exception ex)
            {
                _log.Error("Failed to set clipboard", ex);
            }
        });
    }

    private void PollOnce()
    {
        var now = DateTimeOffset.UtcNow;
        if (_loopGuard.ShouldIgnoreLocalClipboardEvent(now))
            return;

        try
        {
            var text = TryGetClipboardText();
            if (text is null) return;

            if (_lastSeenText is not null && string.Equals(_lastSeenText, text, StringComparison.Ordinal))
                return;

            // Note: debounce/duplicate suppression is applied before publishing (in AgentController).
            _lastSeenText = text;
            TextChanged?.Invoke(this, text);
        }
        catch (Exception ex)
        {
            // Clipboard can be temporarily locked by other processes; keep quiet-ish.
            _log.Warn($"Clipboard read failed: {ex.GetType().Name}");
        }
    }

    private static string? TryGetClipboardText()
    {
        if (!Clipboard.ContainsText()) return null;
        var text = Clipboard.GetText();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}


