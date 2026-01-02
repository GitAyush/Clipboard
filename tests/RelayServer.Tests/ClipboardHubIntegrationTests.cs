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
}


