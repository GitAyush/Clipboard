# ClipboardSync — Drive mode (server relays pointers only)

In **Drive mode**:
- Clipboard **payload never reaches our server**
- Client uploads clipboard text to **Google Drive** (`appDataFolder`)
- Server relays only **pointer/metadata** within a room (`roomId` + `roomSecret`)

## Components
- **Server (notify only)**: `server/RelayServer` (SignalR hub at `/hub/clipboard`)
  - Methods: `JoinRoom`, `ClipboardPointerPublish`
  - Event: `ClipboardPointerChanged`
- **Windows agent**: `clients/ClipboardSync.WindowsAgent`
  - Google OAuth + Drive upload/download
  - Publishes/receives pointer messages (no payload)
- **Protocol**:
  - Pointer DTOs: `ClipboardItemPointer`, `ClipboardPointerPublish`, `ClipboardPointerChanged`

## Build

```powershell
dotnet build .\ClipboardSync.sln -c Release
```

## Google OAuth setup (one-time)
You need an OAuth **Desktop app** client credentials JSON:

1. Google Cloud Console → create/select project
2. Enable **Google Drive API**
3. Configure **OAuth consent screen** (add yourself as a test user if needed)
4. Credentials → **Create Credentials → OAuth client ID** → **Desktop app**
5. Download JSON to e.g. `C:\Users\agar\Downloads\client_secret.json`

## Run (local)

### 1) Start server (bind to all interfaces for Sandbox)

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj -- --urls http://0.0.0.0:5104
```

Allow inbound firewall for port **5104** if prompted.

### 2) Start Windows agent (Drive mode)

```powershell
dotnet run --project .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -- --profile A
```

Tray → Settings:
- **Sync mode** = `Drive`
- **Server URL** = `http://localhost:5104` (host) or `http://<HOST_IP>:5104` (Sandbox)
- **Room ID** = e.g. `room1`
- **Room secret** = e.g. `secret1`
- **Google secrets path** = path to downloaded OAuth client JSON

First run will open a browser for Google sign-in and cache tokens under:
`%AppData%\ClipboardSync\googleTokens\<roomId>\`

## Windows Sandbox (recommended cross-device test on one PC)

### 1) Create a `.wsb` file and disable clipboard redirection
Example: `Sandbox-ClipboardSync.wsb`

```xml
<Configuration>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <Networking>Enable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>C:\Users\agar\Downloads\Clipboard</HostFolder>
      <SandboxFolder>C:\ClipboardSync</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
</Configuration>
```

### 2) Publish the Windows agent as a self-contained EXE (host)
Sandbox often doesn’t have .NET installed, so run a self-contained publish on the host:

```powershell
dotnet publish .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Run this EXE in the sandbox from:
`C:\ClipboardSync\clients\ClipboardSync.WindowsAgent\bin\Release\net8.0-windows\win-x64\publish\ClipboardSync.WindowsAgent.exe`

### 3) Find host IP from inside Sandbox
In Sandbox PowerShell:
- `ipconfig`
- use **Default Gateway** as `<HOST_IP>` (typical Sandbox NAT)

Verify:

```powershell
Invoke-WebRequest http://<HOST_IP>:5104/ | Select-Object -ExpandProperty Content
```

### 4) Configure sandbox agent
Set Server URL to `http://<HOST_IP>:5104` and use the same roomId/secret.

## Automated tests

```powershell
dotnet test .\ClipboardSync.sln -c Release
```

Includes:
- Protocol pointer DTO tests
- Server pointer hub tests (room broadcast + latest-on-join + reconnect)
- Windows agent Drive-mode unit tests (DriveClipboardSync with fake store)


