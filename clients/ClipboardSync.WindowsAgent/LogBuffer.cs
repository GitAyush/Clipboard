using System.Collections.Concurrent;

namespace ClipboardSync.WindowsAgent;

public sealed class LogBuffer
{
    private readonly ConcurrentQueue<string> _lines = new();

    public event Action<string>? LineAdded;

    public IReadOnlyList<string> Snapshot(int maxLines = 500)
    {
        var arr = _lines.ToArray();
        if (arr.Length <= maxLines) return arr;
        return arr[^maxLines..];
    }

    public void Info(string message) => Add("INFO", message);
    public void Warn(string message) => Add("WARN", message);
    public void Error(string message, Exception ex) => Add("ERROR", $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Add(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss} [{level}] {message}";
        _lines.Enqueue(line);
        while (_lines.Count > 2000 && _lines.TryDequeue(out _)) { }
        LineAdded?.Invoke(line);
    }
}


