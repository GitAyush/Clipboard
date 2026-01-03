# ClipboardSync — Developer guide

## Repo layout
- **Shared protocol**: `shared/ClipboardSync.Protocol/`
- **Server**: `server/RelayServer/`
- **Windows agent**: `clients/ClipboardSync.WindowsAgent/`
- **Tests**: `tests/`
- **Docs**: `docs/`
- **Scripts**: `scripts/`
- **Tools (manual helpers)**: `tools/`

## Prerequisites
- .NET SDK 8.x
- Windows (for the WPF tray agent)

## Build

### Server only

```powershell
dotnet build .\server\RelayServer\RelayServer.csproj -c Release
```

### Agent only

```powershell
dotnet build .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -c Release
```

### Whole solution

```powershell
dotnet build .\ClipboardSync.sln -c Release
```

## Test

```powershell
dotnet test .\ClipboardSync.sln -c Release
```

Recommended on Windows (reduces file-lock collisions with running tray/server processes):

```powershell
dotnet test .\ClipboardSync.sln -c Release -p:BaseOutputPath=".\artifacts\test-build\"
```

## Coverage

Run:

```powershell
.\scripts\coverage.ps1
```

Output:
- `.\coverage-report\index.html`

## Publishing the Windows agent

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-agent.ps1
```

Output:
- `.\artifacts\windows-agent\win-x64\`

## Configuration (auth)

### Recommended for local development
- Keep `server/RelayServer/appsettings.json` **safe-by-default** (`Auth:Enabled=false`)
- Put local secrets in `server/RelayServer/appsettings.Development.json` (ignored by git) or environment variables

### Test stability
Some integration tests intentionally validate the non-auth flow. Test hosts force `Auth:Enabled=false` to prevent local machine config from breaking tests.

## Tools

- **Windows Sandbox helper**: `tools/WindowsSandbox/runme.wsb`
  - Used for “two device” testing on a single host (separate clipboard instances).


