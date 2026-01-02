using System.Collections.Concurrent;
using ClipboardSync.Protocol;

namespace RelayServer.Services;

/// <summary>
/// Relay mode history store (room-scoped), in-memory ring buffer.
/// Stores metadata for all items; payload storage for files will be added in later todo.
/// </summary>
public sealed class InMemoryHistoryState
{
    private readonly ConcurrentDictionary<string, RoomHistory> _byRoom = new(StringComparer.Ordinal);

    public IReadOnlyList<HistoryItem> GetHistory(string roomId, int limit)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return Array.Empty<HistoryItem>();
        limit = Math.Clamp(limit, 1, 100);
        return _byRoom.GetOrAdd(roomId, _ => new RoomHistory()).GetLatest(limit);
    }

    public HistoryItem Append(string roomId, HistoryItem item, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("roomId required.", nameof(roomId));
        return _byRoom.GetOrAdd(roomId, _ => new RoomHistory()).Append(item, maxItems);
    }

    public void PutTextPayload(string roomId, string itemId, string text)
    {
        if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentException("roomId required.", nameof(roomId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("itemId required.", nameof(itemId));
        _byRoom.GetOrAdd(roomId, _ => new RoomHistory()).PutTextPayload(itemId, text);
    }

    public string? GetTextPayload(string roomId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        return _byRoom.GetOrAdd(roomId, _ => new RoomHistory()).GetTextPayload(itemId);
    }

    private sealed class RoomHistory
    {
        private readonly object _gate = new();
        private readonly List<HistoryItem> _items = new(); // newest first
        private readonly Dictionary<string, string> _textById = new(StringComparer.Ordinal);

        public HistoryItem Append(HistoryItem item, int maxItems)
        {
            lock (_gate)
            {
                _items.Insert(0, item);
                if (_items.Count > maxItems)
                    _items.RemoveRange(maxItems, _items.Count - maxItems);
                return item;
            }
        }

        public IReadOnlyList<HistoryItem> GetLatest(int limit)
        {
            lock (_gate)
            {
                var count = Math.Min(limit, _items.Count);
                return _items.Take(count).ToArray();
            }
        }

        public void PutTextPayload(string itemId, string text)
        {
            lock (_gate)
            {
                _textById[itemId] = text;
            }
        }

        public string? GetTextPayload(string itemId)
        {
            lock (_gate)
            {
                return _textById.TryGetValue(itemId, out var text) ? text : null;
            }
        }
    }
}


