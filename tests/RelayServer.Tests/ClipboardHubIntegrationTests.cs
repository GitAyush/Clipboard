using ClipboardSync.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace RelayServer.Tests;

public sealed class ClipboardHubIntegrationTests
{
    [Fact]
    public async Task ClipboardPublish_BroadcastsClipboardChanged_ToOtherClient()
    {
        await using var factory = new RelayServerAppFactory();

        // Ensure factory server is created.
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        var received = new TaskCompletionSource<ClipboardChanged>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var receiver = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();

        receiver.On<ClipboardChanged>("ClipboardChanged", msg => received.TrySetResult(msg));
        await receiver.StartAsync();

        await using var sender = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await sender.StartAsync();

        var text = "hello from test";
        var publish = new ClipboardPublish(
            DeviceId: Guid.NewGuid(),
            ClientItemId: Guid.NewGuid(),
            TsClientUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text: text,
            TextHash: ClipboardProtocol.ComputeTextHashUtf8(text));

        await sender.InvokeAsync("ClipboardPublish", publish);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changed = await received.Task.WaitAsync(cts.Token);

        Assert.Equal(text, changed.Text);
        Assert.Equal(publish.DeviceId, changed.OriginDeviceId);
        Assert.True(ClipboardProtocol.HashEquals(publish.TextHash, changed.TextHash));
    }

    [Fact]
    public async Task OnConnected_SendsLatestClipboardChanged_ToNewClient()
    {
        await using var factory = new RelayServerAppFactory();
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        // First client publishes, establishing "latest".
        await using var publisher = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await publisher.StartAsync();

        var text = "latest value";
        var publish = new ClipboardPublish(
            DeviceId: Guid.NewGuid(),
            ClientItemId: Guid.NewGuid(),
            TsClientUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Text: text,
            TextHash: ClipboardProtocol.ComputeTextHashUtf8(text));
        await publisher.InvokeAsync("ClipboardPublish", publish);

        // Second client connects and should immediately receive latest via OnConnectedAsync.
        var received = new TaskCompletionSource<ClipboardChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var lateJoiner = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        lateJoiner.On<ClipboardChanged>("ClipboardChanged", msg => received.TrySetResult(msg));
        await lateJoiner.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changed = await received.Task.WaitAsync(cts.Token);

        Assert.Equal(text, changed.Text);
    }

    [Fact]
    public async Task HubConnection_Reconnects_AfterServerRestart()
    {
        // This test uses a real Kestrel-hosted RelayServer instance (not TestServer),
        // so we can simulate a server restart and validate SignalR automatic reconnect.

        var port = TestPort.GetFreeTcpPort();
        var baseUrl = new Uri($"http://127.0.0.1:{port}");
        var hubUrl = new Uri(baseUrl, "/hub/clipboard");

        // Start server (Kestrel).
        var app1 = global::RelayServer.RelayServerApp.Build(Array.Empty<string>());
        app1.Urls.Clear();
        app1.Urls.Add(baseUrl.ToString());
        await app1.StartAsync();

        var reconnecting = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var conn = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .WithAutomaticReconnect(new FastRetryPolicy())
            .Build();

        conn.Reconnecting += _ =>
        {
            reconnecting.TrySetResult(null);
            return Task.CompletedTask;
        };
        conn.Reconnected += _ =>
        {
            reconnected.TrySetResult(null);
            return Task.CompletedTask;
        };

        await conn.StartAsync();

        // Stop server to force disconnect.
        await app1.StopAsync();
        await app1.DisposeAsync();

        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await reconnecting.Task.WaitAsync(cts1.Token);

        // Restart server on same URL/port.
        var app2 = global::RelayServer.RelayServerApp.Build(Array.Empty<string>());
        app2.Urls.Clear();
        app2.Urls.Add(baseUrl.ToString());
        await app2.StartAsync();

        try
        {
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await reconnected.Task.WaitAsync(cts2.Token);
        }
        finally
        {
            await app2.StopAsync();
            await app2.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClipboardPointerPublish_BroadcastsPointerChanged_WithinRoom()
    {
        await using var factory = new RelayServerAppFactory();
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        const string roomId = "room-test";
        const string roomSecret = "secret";

        var received = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var receiver = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        receiver.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => received.TrySetResult(msg));
        await receiver.StartAsync();
        await receiver.InvokeAsync("JoinRoom", roomId, roomSecret);

        await using var sender = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await sender.StartAsync();
        await sender.InvokeAsync("JoinRoom", roomId, roomSecret);

        var p = new ClipboardItemPointer(
            RoomId: roomId,
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/room-test/1.bin",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("hello"),
            SizeBytes: 5,
            ContentType: "text");

        await sender.InvokeAsync("ClipboardPointerPublish", new ClipboardPointerPublish(p));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changed = await received.Task.WaitAsync(cts.Token);

        Assert.Equal(roomId, changed.Pointer.RoomId);
        Assert.Equal(p.ObjectKey, changed.Pointer.ObjectKey);
        Assert.Equal(p.ProviderFileId, changed.Pointer.ProviderFileId);
        Assert.True(ClipboardProtocol.HashEquals(p.ContentHash, changed.Pointer.ContentHash));
    }

    [Fact]
    public async Task JoinRoom_SendsLatestPointer_ToNewJoiner()
    {
        await using var factory = new RelayServerAppFactory();
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        const string roomId = "room-latest";
        const string roomSecret = "secret";

        // Publisher joins and publishes, establishing "latest" for the room.
        await using var publisher = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await publisher.StartAsync();
        await publisher.InvokeAsync("JoinRoom", roomId, roomSecret);

        var p = new ClipboardItemPointer(
            RoomId: roomId,
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/room-latest/1.bin",
            ProviderFileId: null,
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("latest"),
            SizeBytes: 6,
            ContentType: "text");
        await publisher.InvokeAsync("ClipboardPointerPublish", new ClipboardPointerPublish(p));

        // Late joiner connects, subscribes, then joins; should receive latest pointer on join.
        var received = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var lateJoiner = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        lateJoiner.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => received.TrySetResult(msg));
        await lateJoiner.StartAsync();
        await lateJoiner.InvokeAsync("JoinRoom", roomId, roomSecret);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var changed = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(p.ObjectKey, changed.Pointer.ObjectKey);
    }

    [Fact]
    public async Task JoinRoom_WithWrongSecret_Throws()
    {
        await using var factory = new RelayServerAppFactory();
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        const string roomId = "room-secret-test";

        await using var c1 = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await c1.StartAsync();
        await c1.InvokeAsync("JoinRoom", roomId, "secretA");

        await using var c2 = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await c2.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => c2.InvokeAsync("JoinRoom", roomId, "secretB"));
    }

    [Fact]
    public async Task ClipboardPointerPublish_RequiresJoinRoom()
    {
        await using var factory = new RelayServerAppFactory();
        _ = factory.CreateClient();
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        await using var sender = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();
        await sender.StartAsync();

        var p = new ClipboardItemPointer(
            RoomId: "roomX",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/roomX/1.bin",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("x"),
            SizeBytes: 1,
            ContentType: "text");

        await Assert.ThrowsAsync<HubException>(() => sender.InvokeAsync("ClipboardPointerPublish", new ClipboardPointerPublish(p)));
    }
}


