using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent.Clipboard;

[ExcludeFromCodeCoverage]
public sealed class WindowsClipboardPoller : IDisposable, Drive.IClipboardApplier
{
    private readonly AppSettingsSnapshot _settings;
    private readonly ClipboardLoopGuard _loopGuard;
    private readonly LogBuffer _log;

    private readonly DispatcherTimer _timer;
    private string? _lastSeenText;
    private int _consecutiveReadFailures;
    private DateTimeOffset _lastReadFailureLogUtc = DateTimeOffset.MinValue;

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
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var now = DateTimeOffset.UtcNow;
            _loopGuard.BeginRemoteApply(now);

            try
            {
                var current = TryGetClipboardTextWithRetry(retries: 1, delayMs: 15);
                if (current is not null && string.Equals(current, text, StringComparison.Ordinal))
                {
                    _log.Info("Remote clipboard text already matches current; skipping set.");
                    return;
                }

                SetClipboardTextWithRetry(text, retries: 5, delayMs: 40);
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

        var text = TryGetClipboardTextWithRetry(retries: 3, delayMs: 15);
        if (text is null) return;

        if (_lastSeenText is not null && string.Equals(_lastSeenText, text, StringComparison.Ordinal))
            return;

        // Note: debounce/duplicate suppression is applied before publishing (in AgentController).
        _lastSeenText = text;
        TextChanged?.Invoke(this, text);
    }

    private string? TryGetClipboardTextWithRetry(int retries, int delayMs)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return null;
                var text = System.Windows.Clipboard.GetText();
                _consecutiveReadFailures = 0;
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception) when (attempt < retries)
            {
                Thread.Sleep(delayMs);
            }
            catch (Exception ex)
            {
                // This is commonly COMException when another process holds the clipboard open.
                _consecutiveReadFailures++;

                var nowUtc = DateTimeOffset.UtcNow;
                // Avoid log spam: log at most once every ~5s while failing.
                if (nowUtc - _lastReadFailureLogUtc > TimeSpan.FromSeconds(5))
                {
                    _lastReadFailureLogUtc = nowUtc;
                    _log.Warn($"Clipboard read failed ({_consecutiveReadFailures}x): {ex.GetType().Name}");
                }
                return null;
            }
        }

        return null;
    }

    private static void SetClipboardTextWithRetry(string text, int retries, int delayMs)
    {
        for (int attempt = 0; attempt <= retries; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return;
            }
            catch (Exception) when (attempt < retries)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}


