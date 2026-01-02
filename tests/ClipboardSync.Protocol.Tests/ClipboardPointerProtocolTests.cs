using ClipboardSync.Protocol;
using MessagePack;

namespace ClipboardSync.Protocol.Tests;

public sealed class ClipboardPointerProtocolTests
{
    [Fact]
    public void ClipboardItemPointer_MessagePack_RoundTrips()
    {
        var p = new ClipboardItemPointer(
            RoomId: "roomA",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/roomA/123_device_hash.bin",
            ProviderFileId: "driveFileId123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("hello"),
            SizeBytes: 123,
            ContentType: "text"
        );

        var bytes = MessagePackSerializer.Serialize(p);
        var rt = MessagePackSerializer.Deserialize<ClipboardItemPointer>(bytes);

        Assert.Equal(p.RoomId, rt.RoomId);
        Assert.Equal(p.OriginDeviceId, rt.OriginDeviceId);
        Assert.Equal(p.TsUtcMs, rt.TsUtcMs);
        Assert.Equal(p.ObjectKey, rt.ObjectKey);
        Assert.Equal(p.ProviderFileId, rt.ProviderFileId);
        Assert.True(ClipboardProtocol.HashEquals(p.ContentHash, rt.ContentHash));
        Assert.Equal(p.SizeBytes, rt.SizeBytes);
        Assert.Equal(p.ContentType, rt.ContentType);
    }

    [Fact]
    public void PointerMessages_DoNotContainPayloadFields()
    {
        // Guardrail: ensure the pointer DTOs are "metadata only" (no Text field).
        var names = typeof(ClipboardItemPointer).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Text", names);
        Assert.DoesNotContain("Ciphertext", names);
        Assert.DoesNotContain("Plaintext", names);
    }
}


