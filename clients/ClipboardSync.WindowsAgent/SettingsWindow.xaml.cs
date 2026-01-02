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
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var url = (ServerUrlText.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Server URL is required.", "ClipboardSync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.ServerBaseUrl = url;
        _settings.DeviceName = (DeviceNameText.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(_settings.DeviceName))
        {
            _settings.DeviceName = Environment.MachineName;
        }

        _store.Save(_settings);
        MessageBox.Show(this, "Saved. Restart the app for changes to take effect.", "ClipboardSync", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}


