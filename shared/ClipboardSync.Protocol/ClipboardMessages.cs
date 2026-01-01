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


