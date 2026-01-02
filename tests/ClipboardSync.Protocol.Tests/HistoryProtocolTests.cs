using ClipboardSync.Protocol;
using MessagePack;

namespace ClipboardSync.Protocol.Tests;

public sealed class HistoryProtocolTests
{
    [Fact]
    public void HistoryItem_MessagePack_RoundTrips()
    {
        var hash = ClipboardProtocol.ComputeTextHashUtf8("hello");
        var item = new HistoryItem(
            Id: "id1",
            RoomId: "room1",
            Kind: HistoryItemKind.Text,
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Title: "hello",
            Preview: "hello",
            SizeBytes: 5,
            ContentHash: hash,
            ContentType: "text/plain",
            ProviderFileId: null,
            ObjectKey: null);

        var bytes = MessagePackSerializer.Serialize(item);
        var rt = MessagePackSerializer.Deserialize<HistoryItem>(bytes);

        Assert.Equal(item.Id, rt.Id);
        Assert.Equal(item.RoomId, rt.RoomId);
        Assert.Equal(item.Kind, rt.Kind);
        Assert.Equal(item.OriginDeviceId, rt.OriginDeviceId);
        Assert.Equal(item.Title, rt.Title);
        Assert.Equal(item.Preview, rt.Preview);
        Assert.Equal(item.SizeBytes, rt.SizeBytes);
        Assert.True(ClipboardProtocol.HashEquals(item.ContentHash, rt.ContentHash));
        Assert.Equal(item.ContentType, rt.ContentType);
        Assert.Equal(item.ProviderFileId, rt.ProviderFileId);
        Assert.Equal(item.ObjectKey, rt.ObjectKey);
    }

    [Fact]
    public void HistoryList_RoundTrips()
    {
        var item = new HistoryItem(
            Id: "id1",
            RoomId: "room1",
            Kind: HistoryItemKind.File,
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: 123,
            Title: "file.bin",
            Preview: null,
            SizeBytes: 10,
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
            ContentType: "application/octet-stream",
            ProviderFileId: "fileId",
            ObjectKey: "clips/room1/file.bin");

        var list = new HistoryList("room1", new[] { item });
        var bytes = MessagePackSerializer.Serialize(list);
        var rt = MessagePackSerializer.Deserialize<HistoryList>(bytes);

        Assert.Equal("room1", rt.RoomId);
        Assert.Single(rt.Items);
        Assert.Equal("file.bin", rt.Items[0].Title);
    }
}


