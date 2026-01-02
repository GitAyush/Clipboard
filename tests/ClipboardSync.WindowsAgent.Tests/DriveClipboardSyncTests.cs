using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent;
using ClipboardSync.WindowsAgent.Drive;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class DriveClipboardSyncTests
{
    [Fact]
    public async Task OnLocalTextChanged_UploadsToDrive_AndPublishesPointer()
    {
        var settings = new AppSettingsSnapshot(
            DeviceId: Guid.NewGuid(),
            DeviceName: "dev",
            ServerBaseUrl: "http://localhost:5104",
            SyncMode: "Drive",
            RoomId: "room1",
            RoomSecret: "secret",
            GoogleClientSecretsPath: "ignored");

        var relay = new FakeRelay();
        var clipboard = new FakeClipboard();
        var guard = new ClipboardLoopGuard(localDebounceWindow: TimeSpan.FromMilliseconds(0));
        var log = new LogBuffer();

        var store = new FakeDriveStore();

        var sync = new DriveClipboardSync(settings, relay, clipboard, guard, log, store);
        sync.Start();

        await sync.OnLocalTextChangedAsync("hello");

        Assert.Equal(1, relay.JoinCalls);
        Assert.Equal(1, relay.PublishPointerCalls);
        Assert.NotNull(relay.LastPointer);
        Assert.Equal("room1", relay.LastPointer!.Pointer.RoomId);
        Assert.Equal(store.LastUploadedFileId, relay.LastPointer.Pointer.ProviderFileId);
        Assert.Equal("text", relay.LastPointer.Pointer.ContentType);
    }

    [Fact]
    public async Task OnPointerChanged_DownloadsAndApplies_WhenNotSelf()
    {
        var settings = new AppSettingsSnapshot(
            DeviceId: Guid.NewGuid(),
            DeviceName: "dev",
            ServerBaseUrl: "http://localhost:5104",
            SyncMode: "Drive",
            RoomId: "room1",
            RoomSecret: "secret",
            GoogleClientSecretsPath: "ignored");

        var relay = new FakeRelay();
        var clipboard = new FakeClipboard();
        var guard = new ClipboardLoopGuard(localDebounceWindow: TimeSpan.FromMilliseconds(0));
        var log = new LogBuffer();

        var store = new FakeDriveStore();
        store.DownloadTextResult = "from-drive";

        var sync = new DriveClipboardSync(settings, relay, clipboard, guard, log, store);
        sync.Start();

        var pointer = new ClipboardItemPointer(
            RoomId: "room1",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/room1/x.txt",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
            SizeBytes: 1,
            ContentType: "text");

        await sync.OnPointerChangedAsync(pointer);

        Assert.Equal(1, store.DownloadCalls);
        Assert.Equal("from-drive", clipboard.LastAppliedText);
    }

    [Fact]
    public async Task OnPointerChanged_IgnoresSelf()
    {
        var deviceId = Guid.NewGuid();
        var settings = new AppSettingsSnapshot(
            DeviceId: deviceId,
            DeviceName: "dev",
            ServerBaseUrl: "http://localhost:5104",
            SyncMode: "Drive",
            RoomId: "room1",
            RoomSecret: "secret",
            GoogleClientSecretsPath: "ignored");

        var relay = new FakeRelay();
        var clipboard = new FakeClipboard();
        var guard = new ClipboardLoopGuard(localDebounceWindow: TimeSpan.FromMilliseconds(0));
        var log = new LogBuffer();

        var store = new FakeDriveStore();

        var sync = new DriveClipboardSync(settings, relay, clipboard, guard, log, store);
        sync.Start();

        var pointer = new ClipboardItemPointer(
            RoomId: "room1",
            OriginDeviceId: deviceId,
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/room1/x.txt",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
            SizeBytes: 1,
            ContentType: "text");

        await sync.OnPointerChangedAsync(pointer);

        Assert.Equal(0, store.DownloadCalls);
        Assert.Null(clipboard.LastAppliedText);
    }

    private sealed class FakeRelay : IRelayPointerTransport
    {
        public int JoinCalls { get; private set; }
        public int PublishPointerCalls { get; private set; }
        public ClipboardPointerPublish? LastPointer { get; private set; }

        public Task JoinRoomAsync(string roomId, string roomSecret)
        {
            JoinCalls++;
            return Task.CompletedTask;
        }

        public Task PublishPointerAsync(ClipboardPointerPublish publish)
        {
            PublishPointerCalls++;
            LastPointer = publish;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboard : IClipboardApplier
    {
        public string? LastAppliedText { get; private set; }
        public void ApplyRemoteText(string text) => LastAppliedText = text;
    }

    private sealed class FakeDriveStore : IDriveClipboardStore
    {
        public int UploadCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public string? LastObjectKey { get; private set; }
        public string LastUploadedFileId { get; private set; } = "fileIdFake";
        public string DownloadTextResult { get; set; } = "downloaded";

        public Task<(string fileId, long sizeBytes)> UploadTextAsync(string objectKey, string text, CancellationToken ct)
        {
            UploadCalls++;
            LastObjectKey = objectKey;
            return Task.FromResult((LastUploadedFileId, (long)text.Length));
        }

        public Task<string> DownloadTextAsync(string fileId, CancellationToken ct)
        {
            DownloadCalls++;
            return Task.FromResult(DownloadTextResult);
        }
    }
}


