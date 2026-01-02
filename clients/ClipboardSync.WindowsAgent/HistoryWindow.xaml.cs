using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent;

public partial class HistoryWindow : Window
{
    private readonly AgentController _controller;
    private readonly ObservableCollection<HistoryItem> _items = new();
    private HistoryItem? _selected;

    public HistoryWindow(AgentController controller)
    {
        InitializeComponent();
        _controller = controller;
        HistoryGrid.ItemsSource = _items;

        Closing += (_, e) =>
        {
            if (AppExitState.IsExiting) return;
            e.Cancel = true;
            Hide();
        };

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "Loading...";
            var items = await _controller.GetRemoteHistoryAsync(25);

            _items.Clear();
            foreach (var it in items)
                _items.Add(it);

            StatusText.Text = $"Loaded {_items.Count} item(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void HistoryGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = HistoryGrid.SelectedItem as HistoryItem;
        CopyButton.IsEnabled = _selected?.Kind == HistoryItemKind.Text;
        DownloadButton.IsEnabled = _selected?.Kind == HistoryItemKind.File;
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        await _controller.CopyHistoryItemToClipboardAsync(_selected);
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (_selected.Kind != HistoryItemKind.File) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _selected.Title,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dlg.ShowDialog(this) != true) return;

        var target = dlg.FileName;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await _controller.DownloadHistoryFileAsync(_selected, target);
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}


