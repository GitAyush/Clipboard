using ClipboardSync.Protocol;
using MessagePack;

namespace ClipboardSync.Protocol.Tests;

public sealed class ClipboardFileProtocolTests
{
    [Fact]
    public void ClipboardFilePublish_MessagePack_RoundTrips()
    {
        var publish = new ClipboardFilePublish(
            DeviceId: Guid.NewGuid(),
            FileName: "a.txt",
            ContentType: "text/plain",
            Bytes: "hi"u8.ToArray());

        var bytes = MessagePackSerializer.Serialize(publish);
        var rt = MessagePackSerializer.Deserialize<ClipboardFilePublish>(bytes);

        Assert.Equal(publish.DeviceId, rt.DeviceId);
        Assert.Equal(publish.FileName, rt.FileName);
        Assert.Equal(publish.ContentType, rt.ContentType);
        Assert.Equal(publish.Bytes, rt.Bytes);
    }
}


