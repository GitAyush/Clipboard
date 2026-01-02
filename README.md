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

### Build (all)

```powershell
dotnet build .\ClipboardSync.sln -c Release
```

### Run tests (all)

```powershell
dotnet test .\ClipboardSync.sln -c Release
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


