# ClipboardSync

System-wide clipboard sync with two supported variants:

- **Relay mode** (server relays clipboard payload): see `README.relay.md`
- **Drive mode** (server relays pointers only; payload stored in Google Drive): see `README.drive.md`

## Repo layout

- **Shared protocol**: `shared/ClipboardSync.Protocol/`
- **Relay/Notification server**: `server/RelayServer/`
- **Windows system-wide agent** (tray app): `clients/ClipboardSync.WindowsAgent/`
- **Tests**: `tests/`
- **Diagrams/docs**: `docs/`

## Quick start (common)

### Prerequisites
- .NET SDK 8.x
- Windows (for the WPF tray agent)

### Build & Run (server)

```powershell
dotnet build .\server\RelayServer\RelayServer.csproj -c Release
dotnet run --project .\server\RelayServer\RelayServer.csproj -c Release -- --urls http://0.0.0.0:5104
```

### Build & Run (Client)

```powershell
dotnet build .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -c Release
.\clients\ClipboardSync.WindowsAgent\bin\Release\net8.0-windows\ClipboardSync.WindowsAgent.exe
```

### Build (all)

```powershell
dotnet build .\ClipboardSync.sln -c Release
dotnet test .\ClipboardSync.sln -c Release -p:BaseOutputPath=".\artifacts\test-build\"
```

### Run tests (all)

```powershell
dotnet test .\ClipboardSync.sln -c Release
```

### Publish Windows agent (self-contained, single-file)

This stops any running tray process first (to avoid file-lock publish failures), then publishes to `.\artifacts\windows-agent\win-x64\`.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-agent.ps1
```

### Coverage (optional)

```powershell
dotnet test .\ClipboardSync.sln -c Release --collect:"XPlat Code Coverage"
```

### Coverage report (HTML)

```powershell
.\scripts\coverage.ps1
```

This generates `coverage-report\index.html` using `coverlet.runsettings` (excludes pure UI/IO glue via `[ExcludeFromCodeCoverage]`).

## Logical flow diagrams

See `docs/LOGICAL_FLOW.md`.


