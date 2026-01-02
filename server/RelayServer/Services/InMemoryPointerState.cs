using System.Collections.Concurrent;
using ClipboardSync.Protocol;

namespace RelayServer.Services;

/// <summary>
/// Pointer-only mode: store the latest pointer per room in memory so late joiners converge quickly.
/// </summary>
public sealed class InMemoryPointerState
{
    private readonly ConcurrentDictionary<string, ClipboardItemPointer> _latestByRoom = new(StringComparer.Ordinal);

    public ClipboardItemPointer? GetLatest(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        return _latestByRoom.TryGetValue(roomId, out var p) ? p : null;
    }

    public void SetLatest(ClipboardItemPointer pointer)
    {
        _latestByRoom[pointer.RoomId] = pointer;
    }
}


