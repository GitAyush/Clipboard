# ClipboardSync — User guide

ClipboardSync syncs your clipboard across devices by using:
- A small **RelayServer** (SignalR) for real-time notifications
- A Windows tray app (**WindowsAgent**) that reads/writes your clipboard

It supports two sync modes:
- **Relay mode**: server relays clipboard payload (text, and optional file upload via server)
- **Drive mode**: clipboard payload is stored in **your Google Drive** (`appDataFolder`); server relays **pointers only**

## Quick start (most users)

### 1) Start the server

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj -c Release -- --urls http://0.0.0.0:5104
```

If Windows Firewall prompts, allow inbound connections for port **5104**.

### 2) Start the Windows agent

```powershell
.\clients\ClipboardSync.WindowsAgent\bin\Release\net8.0-windows\ClipboardSync.WindowsAgent.exe
```

Tray → **Settings**:
- **Server URL**: `http://localhost:5104` (same machine) or `http://<HOST_IP>:5104` (another machine / Sandbox)
- Choose **Sync mode**: `Relay` or `Drive`

## Using Relay mode

Relay mode is the simplest: the server receives clipboard text and broadcasts it.

See `README.relay.md` for details.

## Using Drive mode

Drive mode never uploads clipboard payloads to the RelayServer. Payloads are stored in your Google Drive.

See `README.drive.md` for details (OAuth setup, Sandbox testing, etc.).

## Google sign-in (optional, recommended for “same account sync”)

If you enable Google-based sharing:
- Devices signed into the **same Google account** will sync together
- Rooms remain useful for testing/segmentation (within an account)

This requires enabling server auth and turning on “Use Google account for authentication” in the agent.

## Troubleshooting

### “RelayServer auth is DISABLED”
- Your server is running with `Auth:Enabled=false` (or you’re running an older server build).
- Fix: enable auth in your local server config (see `README.drive.md`) and restart the server.

### Sandbox can’t reach server
- Don’t use `localhost` inside Sandbox.
- Bind server to `0.0.0.0` and use `http://<HOST_IP>:5104` from Sandbox.


