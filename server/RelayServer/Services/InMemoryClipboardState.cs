using System.Collections.Concurrent;
using ClipboardSync.Protocol;

namespace RelayServer.Services;

/// <summary>
/// Ephemeral in-memory clipboard state (latest item per room).
/// Used to push the latest clipboard value to newly joined clients.
/// </summary>
public sealed class InMemoryClipboardState
{
    private readonly ConcurrentDictionary<string, ClipboardChanged> _latestByRoom = new(StringComparer.Ordinal);

    public ClipboardChanged? GetLatest(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        return _latestByRoom.TryGetValue(roomId, out var v) ? v : null;
    }

    public void SetLatest(string roomId, ClipboardChanged changed)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return;
        _latestByRoom[roomId] = changed;
    }
}


