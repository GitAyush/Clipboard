using System.Windows;
using ClipboardSync.WindowsAgent.Settings;

namespace ClipboardSync.WindowsAgent;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    public SettingsWindow(SettingsStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        InitializeComponent();

        ServerUrlText.Text = _settings.ServerBaseUrl;
        DeviceNameText.Text = _settings.DeviceName;
        DeviceIdText.Text = _settings.DeviceId.ToString();
        RoomIdText.Text = _settings.RoomId;
        RoomSecretText.Text = _settings.RoomSecret;
        GoogleSecretsPathText.Text = _settings.GoogleClientSecretsPath;

        // SyncModeCombo selection
        var mode = (_settings.SyncMode ?? "Relay").Trim();
        SyncModeCombo.SelectedIndex = string.Equals(mode, "Drive", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

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

        _settings.PublishLocalClipboard = PublishEnabledCheck.IsChecked == true;
        _store.Save(_settings);
        System.Windows.MessageBox.Show(this, "Saved. Some changes may require restarting the app.", "ClipboardSync", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}


