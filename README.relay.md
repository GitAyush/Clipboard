# ClipboardSync — Relay mode (server relays payload)

In **Relay mode**, the server receives the clipboard **text payload** and broadcasts it to connected clients.

## Components
- **Server**: `server/RelayServer` (SignalR hub at `/hub/clipboard`)
- **Windows agent**: `clients/ClipboardSync.WindowsAgent` (WPF tray app)
- **Protocol**: `ClipboardPublish` / `ClipboardChanged` (MessagePack)

## Build

```powershell
dotnet build .\ClipboardSync.sln -c Release
```

## Run (local)

### 1) Start server

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj
```

Default dev URL: `http://localhost:5104/`

### 2) Start Windows agent (Relay mode)

```powershell
dotnet run --project .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -- --profile A
```

In tray **Settings**:
- **Sync mode** = `Relay`
- **Server URL** = `http://localhost:5104`

### Tray options
- **Restart**: relaunches the agent with the same args (including `--profile`) and exits the current instance.
- **Publish local clipboard**: ON/OFF (helps 1-PC testing).

## One-PC testing (single clipboard)
Windows has one clipboard per logged-in session, so you can’t fully simulate “two devices” on one desktop.

Recommended:
1. Run two instances: `--profile A` and `--profile B`
2. Set **Publish local clipboard = OFF** on B
3. Copy text in any app
4. Verify B logs it received the server broadcast

## Windows Sandbox testing (true cross-instance)
Use Windows Sandbox as a second Windows environment with a separate clipboard.

See `README.drive.md` for the `.wsb` recipe; it applies here too.

## Automated tests

```powershell
dotnet test .\ClipboardSync.sln -c Release
```

Key coverage:
- Protocol unit tests (hashing, loop guard)
- Relay server integration tests (broadcast, latest-on-connect, reconnect-after-restart)


