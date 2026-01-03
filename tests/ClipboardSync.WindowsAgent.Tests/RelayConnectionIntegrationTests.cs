using ClipboardSync.Protocol;
using ClipboardSync.WindowsAgent;
using ClipboardSync.WindowsAgent.Sync;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClipboardSync.WindowsAgent.Tests;

public sealed class RelayConnectionIntegrationTests
{
    [Fact]
    public async Task RelayConnection_CanConnect_JoinRoom_AndPublishPointer()
    {
        var port = TestPort.GetFreeTcpPort();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");

        // Start server
        var app = global::RelayServer.RelayServerApp.Build(Array.Empty<string>());
        app.Urls.Clear();
        app.Urls.Add(baseUrl.ToString());
        await app.StartAsync();

        try
        {
            var roomId = "room-it";
            var roomSecret = "secret";

            var receiverTcs = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var receiver = new HubConnectionBuilder()
                .WithUrl(new Uri(baseUrl, "/hub/clipboard"), o =>
                {
                    o.Transports = HttpTransportType.LongPolling;
                })
                .AddMessagePackProtocol()
                .Build();

            receiver.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => receiverTcs.TrySetResult(msg));
            await receiver.StartAsync();
            await receiver.InvokeAsync("JoinRoom", roomId, roomSecret);

            var settings = new AppSettingsSnapshot(
                DeviceId: Guid.NewGuid(),
                DeviceName: "dev",
                ServerBaseUrl: baseUrl.ToString(),
                SyncMode: "Drive",
                RoomId: roomId,
                RoomSecret: roomSecret,
                GoogleClientSecretsPath: "",
                UseGoogleAccountAuth: false,
                MaxInlineTextBytes: 64 * 1024,
                MaxUploadBytes: 1 * 1024 * 1024);

            var log = new LogBuffer();
            var relay = new RelayConnection(settings, log);
            await relay.ConnectAsync();
            await relay.JoinRoomAsync(roomId, roomSecret);

            var pointer = new ClipboardItemPointer(
                RoomId: roomId,
                OriginDeviceId: Guid.NewGuid(),
                TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ObjectKey: "clips/room-it/1.txt",
                ProviderFileId: "file123",
                ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
                SizeBytes: 1,
                ContentType: "text");

            await relay.PublishPointerAsync(new ClipboardPointerPublish(pointer));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var received = await receiverTcs.Task.WaitAsync(cts.Token);

            Assert.Equal(pointer.ObjectKey, received.Pointer.ObjectKey);

            await relay.DisconnectAsync();
            relay.Dispose();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}


