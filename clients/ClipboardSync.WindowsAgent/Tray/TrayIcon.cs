using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent.Tray;

[ExcludeFromCodeCoverage]
public sealed class TrayIcon : IDisposable
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly AgentController _controller;

    private NotifyIcon? _icon;
    private SettingsWindow? _settingsWindow;
    private LogWindow? _logWindow;
    private ToolStripMenuItem? _connectionStatusItem;
    private ToolStripMenuItem? _publishEnabledItem;

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

        _controller.StatusChanged += OnStatusChanged;
        OnStatusChanged("Starting");
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

        _publishEnabledItem = new ToolStripMenuItem("Publish local clipboard")
        {
            CheckOnClick = true,
            Checked = _settings.PublishLocalClipboard
        };
        _publishEnabledItem.Click += (_, _) =>
        {
            var enabled = _publishEnabledItem.Checked;
            _controller.SetPublishEnabled(enabled);
            _settings.PublishLocalClipboard = enabled;
            _store.Save(_settings);
        };
        menu.Items.Add(_publishEnabledItem);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        var logItem = new ToolStripMenuItem("Open Log...");
        logItem.Click += (_, _) => ShowLog();
        menu.Items.Add(logItem);

        var restartItem = new ToolStripMenuItem("Restart");
        restartItem.Click += (_, _) => RestartSelf();
        menu.Items.Add(restartItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private static void RestartSelf()
    {
        try
        {
            var psi = RestartHelper.CreateRestartStartInfo();
            Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Restart failed: {ex.Message}", "ClipboardSync");
        }
    }

    private void OnStatusChanged(string status)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_connectionStatusItem is not null)
                    _connectionStatusItem.Text = $"Status: {status}";
                if (_icon is not null)
                    _icon.Text = $"ClipboardSync ({status})";
            });
        }
        catch
        {
            // ignore shutdown races
        }
    }

    private void ShowSettings()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow ??= new SettingsWindow(_store, _settings);
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private void ShowLog()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _logWindow ??= new LogWindow(_controller.Log);
            _logWindow.Show();
            _logWindow.Activate();
        });
    }

    public void Dispose()
    {
        _controller.StatusChanged -= OnStatusChanged;

        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}


