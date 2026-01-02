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
    private HistoryWindow? _historyWindow;
    private ToolStripMenuItem? _connectionStatusItem;
    private ToolStripMenuItem? _publishEnabledItem;
    private ToolStripMenuItem? _recentMenu;

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

        _recentMenu = new ToolStripMenuItem("Recent (last 5)");
        _recentMenu.DropDownOpening += async (_, _) => await PopulateRecentAsync();
        menu.Items.Add(_recentMenu);

        var historyItem = new ToolStripMenuItem("Open History...");
        historyItem.Click += (_, _) => ShowHistory();
        menu.Items.Add(historyItem);

        var uploadFileItem = new ToolStripMenuItem("Upload file...");
        uploadFileItem.Click += async (_, _) => await UploadFileAsync();
        menu.Items.Add(uploadFileItem);

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
        exitItem.Click += (_, _) =>
        {
            AppExitState.IsExiting = true;
            System.Windows.Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private static void RestartSelf()
    {
        try
        {
            var psi = RestartHelper.CreateRestartStartInfo();
            Process.Start(psi);
            AppExitState.IsExiting = true;
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

    private void ShowHistory()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _historyWindow ??= new HistoryWindow(_controller);
            _historyWindow.Show();
            _historyWindow.Activate();
        });
    }

    private async Task PopulateRecentAsync()
    {
        if (_recentMenu is null) return;

        try
        {
<<<<<<< Current (Your changes)
            _recentMenu.DropDownItems.Clear();
            var items = await _controller.GetRemoteHistoryAsync(5);

            if (items.Length == 0)
            {
                _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
                return;
            }

            foreach (var it in items)
            {
                var label = it.Title;
                if (string.IsNullOrWhiteSpace(label)) label = it.Id;
                if (label.Length > 70) label = label.Substring(0, 70) + "...";

                var mi = new ToolStripMenuItem(label)
                {
                    Enabled = it.Kind == ClipboardSync.Protocol.HistoryItemKind.Text
                };

                mi.Click += async (_, _) => await _controller.CopyHistoryItemToClipboardAsync(it);
                _recentMenu.DropDownItems.Add(mi);
            }
        }
        catch (Exception ex)
        {
            _recentMenu.DropDownItems.Clear();
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem($"(error: {ex.Message})") { Enabled = false });
=======
            // Show something immediately; we'll replace after async load.
            _recentMenu.DropDownItems.Clear();
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(loading...)") { Enabled = false });

            var items = await _controller.GetRemoteHistoryAsync(5);

            // Ensure we mutate WinForms menu items on the UI thread (avoids "empty" menu due to cross-thread updates).
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _recentMenu.DropDownItems.Clear();

                if (items.Length == 0)
                {
                    _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
                    return;
                }

                foreach (var it in items)
                {
                    var label = it.Title;
                    if (string.IsNullOrWhiteSpace(label)) label = it.Id;
                    if (label.Length > 70) label = label.Substring(0, 70) + "...";

                    var mi = new ToolStripMenuItem(label)
                    {
                        Enabled = it.Kind == ClipboardSync.Protocol.HistoryItemKind.Text
                    };

                    mi.Click += async (_, _) => await _controller.CopyHistoryItemToClipboardAsync(it);
                    _recentMenu.DropDownItems.Add(mi);
                }
            });
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _recentMenu.DropDownItems.Clear();
                _recentMenu.DropDownItems.Add(new ToolStripMenuItem($"(error: {ex.Message})") { Enabled = false });
            });
>>>>>>> Incoming (Background Agent changes)
        }
    }

    private async Task UploadFileAsync()
    {
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Upload file to ClipboardSync",
                Multiselect = false
            };

            if (dlg.ShowDialog() != DialogResult.OK) return;
            await _controller.UploadFileAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Upload failed: {ex.Message}", "ClipboardSync");
        }
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


