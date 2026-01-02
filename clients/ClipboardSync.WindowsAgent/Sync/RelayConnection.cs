using ClipboardSync.Protocol;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ClipboardSync.WindowsAgent.Sync;

public sealed class RelayConnection : IDisposable
    , Drive.IRelayPointerTransport
{
    private const string ClipboardChangedMethod = "ClipboardChanged";
    private const string ClipboardPublishMethod = "ClipboardPublish";
    private const string ClipboardPointerChangedMethod = "ClipboardPointerChanged";
    private const string JoinRoomMethod = "JoinRoom";
    private const string ClipboardPointerPublishMethod = "ClipboardPointerPublish";

    private readonly AppSettingsSnapshot _settings;
    private readonly LogBuffer _log;

    private readonly object _gate = new();
    private HubConnection? _connection;

    public event Action<ClipboardChanged>? ClipboardChanged;
    public event Action<ClipboardPointerChanged>? ClipboardPointerChanged;
    public event Action<string>? StatusChanged;

    public RelayConnection(AppSettingsSnapshot settings, LogBuffer log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task ConnectAsync()
    {
        HubConnection conn;

        lock (_gate)
        {
            if (_connection is not null && _connection.State is HubConnectionState.Connected or HubConnectionState.Connecting)
                return;

            var hubUrl = BuildHubUrl(_settings.ServerBaseUrl);

            conn = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .AddMessagePackProtocol()
                .WithAutomaticReconnect()
                .Build();

            conn.On<ClipboardChanged>(ClipboardChangedMethod, changed =>
            {
                _log.Info($"Received ClipboardChanged origin={changed.OriginDeviceId} textLen={changed.Text?.Length ?? 0} hash={ClipboardProtocol.HashToHex(changed.TextHash)}");
                ClipboardChanged?.Invoke(changed);
            });

            conn.On<ClipboardPointerChanged>(ClipboardPointerChangedMethod, changed =>
            {
                _log.Info($"Received ClipboardPointerChanged roomId={changed.Pointer.RoomId} origin={changed.Pointer.OriginDeviceId} key={changed.Pointer.ObjectKey}");
                ClipboardPointerChanged?.Invoke(changed);
            });

            conn.Reconnecting += error =>
            {
                _log.Warn($"Relay reconnecting... ({error?.Message ?? "unknown error"})");
                StatusChanged?.Invoke("Reconnecting");
                return Task.CompletedTask;
            };
            conn.Reconnected += connectionId =>
            {
                _log.Info($"Relay reconnected. connectionId={connectionId}");
                StatusChanged?.Invoke("Connected");
                return Task.CompletedTask;
            };
            conn.Closed += error =>
            {
                _log.Warn($"Relay closed. ({error?.Message ?? "no error"})");
                StatusChanged?.Invoke("Disconnected");
                return Task.CompletedTask;
            };

            _connection = conn;
        }

        try
        {
            _log.Info("Connecting to relay...");
            StatusChanged?.Invoke("Connecting");
            await conn.StartAsync();
            _log.Info("Connected to relay.");
            StatusChanged?.Invoke("Connected");
        }
        catch (Exception ex)
        {
            _log.Error("Relay connect failed", ex);
            StatusChanged?.Invoke("Error");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        HubConnection? conn;
        lock (_gate) conn = _connection;
        if (conn is null) return;

        try
        {
            await conn.StopAsync();
            StatusChanged?.Invoke("Disconnected");
        }
        catch (Exception ex)
        {
            _log.Error("Relay disconnect failed", ex);
        }
    }

    public Task PublishAsync(ClipboardPublish publish)
    {
        HubConnection? conn;
        lock (_gate) conn = _connection;

        if (conn is null || conn.State != HubConnectionState.Connected)
            return Task.CompletedTask;

        return conn.InvokeAsync(ClipboardPublishMethod, publish);
    }

    public Task JoinRoomAsync(string roomId, string roomSecret)
    {
        HubConnection? conn;
        lock (_gate) conn = _connection;

        if (conn is null || conn.State != HubConnectionState.Connected)
            return Task.CompletedTask;

        return conn.InvokeAsync(JoinRoomMethod, roomId, roomSecret);
    }

    public Task PublishPointerAsync(ClipboardPointerPublish publish)
    {
        HubConnection? conn;
        lock (_gate) conn = _connection;

        if (conn is null || conn.State != HubConnectionState.Connected)
            return Task.CompletedTask;

        return conn.InvokeAsync(ClipboardPointerPublishMethod, publish);
    }

    private static string BuildHubUrl(string serverBaseUrl)
    {
        var baseUrl = (serverBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("ServerBaseUrl is required.");
        return $"{baseUrl}/hub/clipboard";
    }

    public void Dispose()
    {
        HubConnection? conn;
        lock (_gate)
        {
            conn = _connection;
            _connection = null;
        }

        if (conn is not null)
        {
            try { conn.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* ignore */ }
        }
    }
}


