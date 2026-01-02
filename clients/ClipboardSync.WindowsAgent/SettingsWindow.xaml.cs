using System.Diagnostics.CodeAnalysis;
using System.Windows;
using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    public SettingsWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        InitializeComponent();

        Closing += (_, e) =>
        {
            if (AppExitState.IsExiting) return;
            e.Cancel = true;
            Hide();
        };

        ServerUrlText.Text = _settings.ServerBaseUrl;
        DeviceNameText.Text = _settings.DeviceName;
        DeviceIdText.Text = _settings.DeviceId.ToString();
        RoomIdText.Text = _settings.RoomId;
        RoomSecretText.Text = _settings.RoomSecret;
        GoogleSecretsPathText.Text = _settings.GoogleClientSecretsPath;

        // SyncModeCombo selection
        var mode = (_settings.SyncMode ?? "Relay").Trim();
        SyncModeCombo.SelectedIndex = string.Equals(mode, "Drive", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        // MaxInlineTextCombo selection
        MaxInlineTextCombo.SelectedIndex = _settings.MaxInlineTextBytes >= 256 * 1024 ? 1 : 0;

        // MaxUpload in MB
        MaxUploadMbText.Text = Math.Max(1, _settings.MaxUploadBytes / (1024 * 1024)).ToString();

        PublishEnabledCheck.IsChecked = _settings.PublishLocalClipboard;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var url = (ServerUrlText.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            System.Windows.MessageBox.Show(this, "Server URL is required.", "ClipboardSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.ServerBaseUrl = url;
        _settings.DeviceName = (DeviceNameText.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(_settings.DeviceName))
        {
            _settings.DeviceName = Environment.MachineName;
        }

        _settings.SyncMode = ((SyncModeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Relay").Trim();
        _settings.RoomId = (RoomIdText.Text ?? "").Trim();
        _settings.RoomSecret = (RoomSecretText.Text ?? "").Trim();
        _settings.GoogleClientSecretsPath = (GoogleSecretsPathText.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(_settings.RoomId)) _settings.RoomId = "default";
        if (string.IsNullOrWhiteSpace(_settings.SyncMode)) _settings.SyncMode = "Relay";

        // Limits
        var maxInline = MaxInlineTextCombo.SelectedIndex == 1 ? 256 * 1024 : 64 * 1024;
        _settings.MaxInlineTextBytes = maxInline;

        if (!int.TryParse((MaxUploadMbText.Text ?? "").Trim(), out var mb)) mb = 1;
        mb = Math.Clamp(mb, 1, 10);
        _settings.MaxUploadBytes = mb * 1024 * 1024;

        _settings.PublishLocalClipboard = PublishEnabledCheck.IsChecked == true;
        _store.Save(_settings);
        System.Windows.MessageBox.Show(this, "Saved. Some changes may require restarting the app.", "ClipboardSync", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}


