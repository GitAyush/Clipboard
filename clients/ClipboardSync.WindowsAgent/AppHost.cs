using System.Diagnostics.CodeAnalysis;
using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed class AppHost : IDisposable
{
    private Tray.TrayIcon? _tray;
    private AgentController? _controller;

    public void Start(string[] args)
    {
        var profile = ProfileArgs.TryGetProfileName(args);
        var settingsStore = new SettingsStore(profile);
        var settings = settingsStore.LoadOrCreateDefaults();

        _controller = new AgentController(settings);
        _controller.Start();

        _tray = new Tray.TrayIcon(settingsStore, settings, _controller);
        _tray.Initialize();
    }

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;

        _controller?.Dispose();
        _controller = null;
    }
}


