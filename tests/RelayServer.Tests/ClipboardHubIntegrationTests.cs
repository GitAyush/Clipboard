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
}


