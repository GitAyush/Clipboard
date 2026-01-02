using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClipboardSync.Protocol;

namespace RelayServer.Services;

/// <summary>
/// Phase 2 (pointer-only): in-memory room registry with a shared secret.
/// Purpose: scope broadcasts to devices that know the same room secret.
/// </summary>
public sealed class InMemoryRoomRegistry
{
    private readonly ConcurrentDictionary<string, byte[]> _roomSecretHash = new(StringComparer.Ordinal);

    /// <summary>
    /// Ensures the room exists. If it doesn't exist, creates it with the provided secret.
    /// If it exists, validates the provided secret.
    /// </summary>
    public bool EnsureRoomAndValidateSecret(string roomId, string roomSecret)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return false;
        if (string.IsNullOrWhiteSpace(roomSecret)) return false;

        var hash = ComputeSecretHash(roomId, roomSecret);

        var existing = _roomSecretHash.GetOrAdd(roomId, _ => hash);
        return ClipboardProtocol.HashEquals(existing, hash);
    }

    private static byte[] ComputeSecretHash(string roomId, string roomSecret)
    {
        // Salt by roomId to avoid trivial reuse across rooms; still not meant for password storage.
        var bytes = Encoding.UTF8.GetBytes($"{roomId}\n{roomSecret}");
        return SHA256.HashData(bytes);
    }
}


