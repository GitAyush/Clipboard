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
    /// Default client setting: cap inline clipboard text payloads to reduce abuse and memory pressure.
    /// </summary>
    public const int DefaultMaxTextBytesUtf8 = 64 * 1024;

    /// <summary>
    /// Absolute upper bound supported by the protocol/server for inline clipboard text payloads.
    /// Clients may enforce lower limits (e.g., 64KB) via settings.
    /// </summary>
    public const int MaxTextBytesUtf8 = 256 * 1024;

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

public enum HistoryItemKind
{
    Text = 0,
    File = 1
}

/// <summary>
/// History item metadata. In Relay mode the server may store payload; in Drive mode payload lives in Drive.
/// This DTO intentionally supports both modes via optional storage pointers.
/// </summary>
[MessagePackObject]
public sealed record HistoryItem(
    /// <summary>Unique id for the history item (stable within a room).</summary>
    [property: Key(0)] string Id,
    /// <summary>Room/group id that owns this history item.</summary>
    [property: Key(1)] string RoomId,
    /// <summary>Type of item (text/file).</summary>
    [property: Key(2)] HistoryItemKind Kind,
    /// <summary>Origin device that created the item.</summary>
    [property: Key(3)] Guid OriginDeviceId,
    /// <summary>UTC timestamp (ms) for ordering.</summary>
    [property: Key(4)] long TsUtcMs,
    /// <summary>Human-friendly title (e.g., filename or first line of text).</summary>
    [property: Key(5)] string Title,
    /// <summary>Optional preview (truncated text).</summary>
    [property: Key(6)] string? Preview,
    /// <summary>Size of payload in bytes (UTF-8 bytes for text; file size for file).</summary>
    [property: Key(7)] long SizeBytes,
    /// <summary>Hash of content (SHA-256 over UTF-8 text or file bytes).</summary>
    [property: Key(8)] byte[] ContentHash,
    /// <summary>Content type string (e.g., text/plain, application/octet-stream).</summary>
    [property: Key(9)] string ContentType,
    /// <summary>Drive mode: provider file id (e.g., Google Drive fileId). Null for relay text items.</summary>
    [property: Key(10)] string? ProviderFileId,
    /// <summary>Drive mode: opaque object key (client-defined). Null for relay text items.</summary>
    [property: Key(11)] string? ObjectKey
);

[MessagePackObject]
public sealed record HistoryList(
    [property: Key(0)] string RoomId,
    [property: Key(1)] HistoryItem[] Items
);

/// <summary>Client → Server: request history list for the joined room.</summary>
[MessagePackObject]
public sealed record GetHistoryRequest(
    [property: Key(0)] int Limit
);

/// <summary>Server → Client: response containing history list.</summary>
[MessagePackObject]
public sealed record GetHistoryResponse(
    [property: Key(0)] HistoryList History
);

/// <summary>Server → Clients: notify that a new history item was added.</summary>
[MessagePackObject]
public sealed record HistoryItemAdded(
    [property: Key(0)] HistoryItem Item
);

/// <summary>Client → Server: request full text content for a history item (Relay mode).</summary>
[MessagePackObject]
public sealed record GetHistoryTextRequest(
    [property: Key(0)] string ItemId
);

/// <summary>Server → Client: full text for a history item (Relay mode).</summary>
[MessagePackObject]
public sealed record GetHistoryTextResponse(
    [property: Key(0)] string ItemId,
    [property: Key(1)] string Text
);

/// <summary>Client → Server: publish a file payload (Relay mode only).</summary>
[MessagePackObject]
public sealed record ClipboardFilePublish(
    [property: Key(0)] Guid DeviceId,
    [property: Key(1)] string FileName,
    [property: Key(2)] string ContentType,
    [property: Key(3)] byte[] Bytes
);


