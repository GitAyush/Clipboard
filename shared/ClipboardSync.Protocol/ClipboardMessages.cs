using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace ClipboardSync.Protocol;

/// <summary>
/// Phase 1 protocol: text-only clipboard relay messages.
/// Designed for SignalR MessagePack protocol (binary framing).
/// </summary>
public static class ClipboardProtocol
{
    /// <summary>
    /// Phase 1 constraint: cap clipboard text payloads to reduce abuse and memory pressure.
    /// </summary>
    public const int MaxTextBytesUtf8 = 64 * 1024;

    /// <summary>
    /// Computes a stable SHA-256 hash over the UTF-8 bytes of <paramref name="text"/>.
    /// Used by clients for loop prevention (avoid re-sending the same clipboard content).
    /// </summary>
    public static byte[] ComputeTextHashUtf8(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount > MaxTextBytesUtf8)
        {
            throw new ArgumentOutOfRangeException(nameof(text), $"Text exceeds max of {MaxTextBytesUtf8} UTF-8 bytes.");
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = Encoding.UTF8.GetBytes(text, rented);
            return SHA256.HashData(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    public static bool HashEquals(byte[]? a, byte[]? b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string HashToHex(byte[] hash)
    {
        if (hash is null) throw new ArgumentNullException(nameof(hash));
        return Convert.ToHexString(hash);
    }
}

/// <summary>
/// Client → Server: publish local clipboard text.
/// </summary>
[MessagePackObject]
public sealed record ClipboardPublish(
    [property: Key(0)] Guid DeviceId,
    [property: Key(1)] Guid ClientItemId,
    [property: Key(2)] long TsClientUtcMs,
    [property: Key(3)] string Text,
    [property: Key(4)] byte[] TextHash
);

/// <summary>
/// Server → Clients: broadcast clipboard change to all connected clients.
/// </summary>
[MessagePackObject]
public sealed record ClipboardChanged(
    [property: Key(0)] Guid OriginDeviceId,
    [property: Key(1)] long ServerTsUtcMs,
    [property: Key(2)] string Text,
    [property: Key(3)] byte[] TextHash
);

/// <summary>
/// Phase 2+ (Drive-backed / zero-payload-server):
/// Pointer-only notification for a new clipboard item stored outside our server (e.g., Google Drive).
/// Server must never receive clipboard plaintext; it only relays pointers/metadata.
/// </summary>
[MessagePackObject]
public sealed record ClipboardItemPointer(
    /// <summary>Logical group identifier (pairing/room). Used for server-side broadcast scoping.</summary>
    [property: Key(0)] string RoomId,
    /// <summary>Device that originated the clipboard item.</summary>
    [property: Key(1)] Guid OriginDeviceId,
    /// <summary>Monotonic-ish timestamp (UTC ms) used for ordering.</summary>
    [property: Key(2)] long TsUtcMs,
    /// <summary>
    /// Opaque storage key (client-defined). Example: clips/&lt;roomId&gt;/&lt;ts&gt;_&lt;device&gt;_&lt;hash&gt;.bin
    /// </summary>
    [property: Key(3)] string ObjectKey,
    /// <summary>
    /// Provider-specific file identifier (e.g., Google Drive fileId). Optional if ObjectKey is sufficient.
    /// </summary>
    [property: Key(4)] string? ProviderFileId,
    /// <summary>Hash of the clipboard content (typically SHA-256 over plaintext UTF-8 or over ciphertext bytes).</summary>
    [property: Key(5)] byte[] ContentHash,
    /// <summary>Size in bytes of the stored blob (ciphertext size if encrypted).</summary>
    [property: Key(6)] long SizeBytes,
    /// <summary>Content type (text only for now, but extensible).</summary>
    [property: Key(7)] string ContentType
);

/// <summary>
/// Client → Server: publish a pointer to a new clipboard item (no payload).
/// </summary>
[MessagePackObject]
public sealed record ClipboardPointerPublish(
    [property: Key(0)] ClipboardItemPointer Pointer
);

/// <summary>
/// Server → Clients: broadcast a pointer to all devices in the same room.
/// </summary>
[MessagePackObject]
public sealed record ClipboardPointerChanged(
    [property: Key(0)] ClipboardItemPointer Pointer
);


