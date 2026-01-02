using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Windows;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed partial class LogWindow : Window
{
    private readonly LogBuffer _log;
    private readonly StringBuilder _sb = new();

    public LogWindow(LogBuffer log)
    {
        _log = log;
        InitializeComponent();

        foreach (var line in _log.Snapshot())
        {
            _sb.AppendLine(line);
        }
        LogText.Text = _sb.ToString();

        _log.LineAdded += OnLineAdded;
        Closed += (_, _) => _log.LineAdded -= OnLineAdded;
    }

    private void OnLineAdded(string line)
    {
        Dispatcher.Invoke(() =>
        {
            _sb.AppendLine(line);
            LogText.Text = _sb.ToString();
            LogText.ScrollToEnd();
        });
    }
}


