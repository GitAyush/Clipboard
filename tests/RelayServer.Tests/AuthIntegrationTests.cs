using ClipboardSync.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;

namespace RelayServer.Tests;

public sealed class AuthIntegrationTests
{
    [Fact]
    public async Task AuthGoogle_ReturnsServerJwt()
    {
        await using var factory = new AuthTestFactory();
        var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/auth/google", new { accessToken = "userA" });
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.token));
        Assert.Equal("userA", payload.subject);
        Assert.Equal("userA@example.com", payload.email);
        Assert.True(payload.expiresUtcMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Hub_RequiresAuth_WhenEnabled()
    {
        await using var factory = new AuthTestFactory();
        _ = factory.CreateClient();

        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        await using var conn = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddMessagePackProtocol()
            .Build();

        await conn.StartAsync();

        // JoinRoom should fail because we're not authenticated.
        await Assert.ThrowsAsync<HubException>(() => conn.InvokeAsync("JoinRoom", "default", ""));
    }

    [Fact]
    public async Task AuthScopedRooms_DoNotCrossTalkBetweenDifferentGoogleSubjects()
    {
        await using var factory = new AuthTestFactory();
        var http = factory.CreateClient();
        _ = factory.CreateClient(); // ensure server is started

        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        async Task<string> GetJwtAsync(string subject)
        {
            var resp = await http.PostAsJsonAsync("/auth/google", new { accessToken = subject });
            resp.EnsureSuccessStatusCode();
            var payload = await resp.Content.ReadFromJsonAsync<AuthResponse>();
            return payload!.token;
        }

        var jwtA = await GetJwtAsync("userA");
        var jwtB = await GetJwtAsync("userB");

        var gotA = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gotB = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connA = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult(jwtA)!;
            })
            .AddMessagePackProtocol()
            .Build();

        await using var connB = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult(jwtB)!;
            })
            .AddMessagePackProtocol()
            .Build();

        connA.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => gotA.TrySetResult(msg));
        connB.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => gotB.TrySetResult(msg));

        await connA.StartAsync();
        await connB.StartAsync();

        await connA.InvokeAsync("JoinRoom", "default", "");
        await connB.InvokeAsync("JoinRoom", "default", "");

        var p = new ClipboardItemPointer(
            RoomId: "default",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/default/1.txt",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("hello"),
            SizeBytes: 5,
            ContentType: "text");

        // userA publishes; userB must NOT receive it.
        await connA.InvokeAsync("ClipboardPointerPublish", new ClipboardPointerPublish(p));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var receivedA = await gotA.Task.WaitAsync(cts.Token);
        Assert.Equal("default", receivedA.Pointer.RoomId);

        // userB should time out (no cross-talk).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await gotB.Task.WaitAsync(cts2.Token);
        });
    }

    [Fact]
    public async Task ClipboardPointerPublish_WhenAuthEnabled_NormalizesPointerRoomIdToJoinedRoom()
    {
        await using var factory = new AuthTestFactory();
        var http = factory.CreateClient();
        _ = factory.CreateClient(); // ensure server is started

        var hubUrl = new Uri(factory.Server.BaseAddress, "/hub/clipboard");

        var resp = await http.PostAsJsonAsync("/auth/google", new { accessToken = "userA" });
        resp.EnsureSuccessStatusCode();
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        var jwt = auth!.token;

        var received = new TaskCompletionSource<ClipboardPointerChanged>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var conn = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult(jwt)!;
            })
            .AddMessagePackProtocol()
            .Build();

        conn.On<ClipboardPointerChanged>("ClipboardPointerChanged", msg => received.TrySetResult(msg));
        await conn.StartAsync();

        // Join "default", but publish a pointer with a different roomId.
        await conn.InvokeAsync("JoinRoom", "default", "");

        var p = new ClipboardItemPointer(
            RoomId: "room1",
            OriginDeviceId: Guid.NewGuid(),
            TsUtcMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObjectKey: "clips/room1/1.txt",
            ProviderFileId: "file123",
            ContentHash: ClipboardProtocol.ComputeTextHashUtf8("hello"),
            SizeBytes: 5,
            ContentType: "text");

        await conn.InvokeAsync("ClipboardPointerPublish", new ClipboardPointerPublish(p));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var changed = await received.Task.WaitAsync(cts.Token);

        // Server should normalize to the joined room ("default") in auth mode.
        Assert.Equal("default", changed.Pointer.RoomId);
        Assert.Equal(p.ProviderFileId, changed.Pointer.ProviderFileId);
        Assert.True(ClipboardProtocol.HashEquals(p.ContentHash, changed.Pointer.ContentHash));
    }

    private sealed class AuthResponse
    {
        public string token { get; set; } = "";
        public long expiresUtcMs { get; set; }
        public string subject { get; set; } = "";
        public string? email { get; set; }
    }
}


