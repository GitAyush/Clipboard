using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent.Drive;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class DriveHistoryManifestTests
{
    [Fact]
    public void AppendOrReplaceNewestFirst_KeepsMaxItems()
    {
        var m = DriveHistoryManifest.CreateEmpty("room1");
        for (int i = 0; i < 15; i++)
        {
            m.AppendOrReplaceNewestFirst(new DriveHistoryManifestItem { Id = $"id{i}", Title = $"t{i}" }, maxItems: 10);
        }

        Assert.Equal(10, m.Items.Count);
        Assert.Equal("id14", m.Items[0].Id);
    }

    [Fact]
    public void AppendOrReplaceNewestFirst_ReplacesById()
    {
        var m = DriveHistoryManifest.CreateEmpty("room1");
        m.AppendOrReplaceNewestFirst(new DriveHistoryManifestItem { Id = "id1", Title = "a" }, maxItems: 10);
        m.AppendOrReplaceNewestFirst(new DriveHistoryManifestItem { Id = "id1", Title = "b" }, maxItems: 10);

        Assert.Single(m.Items);
        Assert.Equal("b", m.Items[0].Title);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        var p = new ClipboardItemPointer(
            RoomId: "room1",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: 123,
            ObjectKey: "clips/room1/x.txt",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
            SizeBytes: 1,
            ContentType: "text");

        var m = DriveHistoryManifest.CreateEmpty("room1");
        m.AppendOrReplaceNewestFirst(DriveHistoryManifestItem.FromPointer(p, HistoryItemKind.Text, "title", "prev"), maxItems: 10);

        var json = DriveHistoryManifest.Serialize(m);
        var rt = DriveHistoryManifest.Deserialize(json);

        Assert.Equal("room1", rt.RoomId);
        Assert.Single(rt.Items);
        Assert.Equal(m.Items[0].Id, rt.Items[0].Id);
    }
}


