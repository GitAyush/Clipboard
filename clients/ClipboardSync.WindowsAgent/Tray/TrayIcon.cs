using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent.Tray;

public sealed class TrayIcon : IDisposable
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly AgentController _controller;

    private NotifyIcon? _icon;
    private SettingsWindow? _settingsWindow;
    private LogWindow? _logWindow;
    private ToolStripMenuItem? _connectionStatusItem;

    public TrayIcon(SettingsStore store, AppSettings settings, AgentController controller)
    {
        _store = store;
        _settings = settings;
        _controller = controller;
    }

    public void Initialize()
    {
        _icon = new NotifyIcon
        {
            Text = "ClipboardSync (Windows Agent)",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        _icon.DoubleClick += (_, _) => ShowSettings();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _connectionStatusItem = new ToolStripMenuItem("Status: starting...")
        {
            Enabled = false
        };
        menu.Items.Add(_connectionStatusItem);

        var statusItem = new ToolStripMenuItem($"Profile: {Path.GetFileName(_store.SettingsPath)}")
        {
            Enabled = false
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var connectItem = new ToolStripMenuItem("Connect");
        connectItem.Click += async (_, _) => await _controller.ConnectAsync();
        menu.Items.Add(connectItem);

        var disconnectItem = new ToolStripMenuItem("Disconnect");
        disconnectItem.Click += async (_, _) => await _controller.DisconnectAsync();
        menu.Items.Add(disconnectItem);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        var logItem = new ToolStripMenuItem("Open Log...");
        logItem.Click += (_, _) => ShowLog();
        menu.Items.Add(logItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow ??= new SettingsWindow(_store, _settings);
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ShowLog()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logWindow ??= new LogWindow(_controller.Log);
            _logWindow.Show();
            _logWindow.Activate();
        });
    }

    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}


