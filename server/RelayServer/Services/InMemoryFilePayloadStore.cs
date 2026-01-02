using System.Collections.Concurrent;

namespace RelayServer.Services;

/// <summary>
/// Relay mode file payload store (in-memory). Maps (roomId,itemId) to raw bytes.
/// Used by the HTTP download endpoint. Upload path will be added in the file-support todo.
/// </summary>
public sealed class InMemoryFilePayloadStore
{
    private readonly ConcurrentDictionary<string, byte[]> _byKey = new(StringComparer.Ordinal);

    private static string Key(string roomId, string itemId) => $"{roomId}\n{itemId}";

    public void Put(string roomId, string itemId, byte[] bytes)
    {
        _byKey[Key(roomId, itemId)] = bytes;
    }

    public byte[]? Get(string roomId, string itemId)
    {
        return _byKey.TryGetValue(Key(roomId, itemId), out var bytes) ? bytes : null;
    }
}


