# ClipboardSync (MVP)

Cross-device clipboard sync MVP built in phases:

- **Phase 1 (done)**: real-time text clipboard relay (no auth, no encryption, no persistence).
- Next: auth → encryption/storage → additional platforms.

## Major components

- **Shared protocol**: `shared/ClipboardSync.Protocol/`
  - MessagePack DTOs: `ClipboardPublish`, `ClipboardChanged`
  - Helpers: text size cap + hashing (`ClipboardProtocol`)
  - Loop prevention rules: `ClipboardLoopGuard`

- **Relay server (SignalR hub)**: `server/RelayServer/`
  - Hub endpoint: `/hub/clipboard` (SignalR + MessagePack)
  - Behavior:
    - Client publishes clipboard text (`ClipboardPublish`)
    - Server broadcasts `ClipboardChanged` to all clients
    - Server keeps **latest item in-memory** and sends it to newly-connected clients

- **Windows system-wide clipboard agent**: `clients/ClipboardSync.WindowsAgent/`
  - Runs as a tray app
  - Watches OS clipboard (text-only) and publishes changes to the relay
  - Receives relay updates and applies to OS clipboard
  - Loop prevention: ignores self-origin, ignores local events shortly after remote apply, debounce + duplicate suppression

## Build prerequisites

- **.NET SDK 8.x**
- Windows is required to run the Windows agent (WPF).

## Build

From repo root:

```powershell
dotnet build .\server\RelayServer\RelayServer.csproj -c Debug
dotnet build .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -c Debug
```

## Run

### 1) Relay server

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj
```

- Default URL (dev): `http://localhost:5104/`
- Hub: `http://localhost:5104/hub/clipboard`

### 2) Windows agent

```powershell
dotnet run --project .\clients\ClipboardSync.WindowsAgent\ClipboardSync.WindowsAgent.csproj -- --profile A
```

#### Arguments

- `--profile <name>`
  - Uses a different settings file per instance so you can run multiple instances on one PC.
  - Settings are stored under `%AppData%\\ClipboardSync\\settings.<profile>.json`

#### Runtime settings (tray → Settings)

- **Server URL**: base server URL (default `http://localhost:5104`)
- **Device name**: label only (for now)
- **Device ID**: stable GUID per profile (auto-generated)

#### Tray toggles

- **Publish local clipboard**: ON/OFF (persisted per profile)
  - Useful for 1-PC testing: make A publish and B receive-only.

## Testing

### Manual sanity checks

#### One PC (one Windows clipboard)
Because Windows has a single clipboard per logged-in session, two instances on the same PC share the same clipboard.

Recommended 1-PC verification:

1. Run server.
2. Run two agents:
   - `--profile A`
   - `--profile B`
3. On tray icon **B**, set **Publish local clipboard** = OFF.
4. Copy text in any app (Notepad/VS Code/Browser).
5. Open **Open Log…** on both tray icons:
   - A should publish.
   - B should show it **received** `ClipboardChanged` (and may skip setting clipboard because it already matches on one PC).

#### Two PCs (true cross-device)
Run server (one place reachable by both machines), run one agent per machine, then copy on one and paste on the other.

#### Windows Sandbox (true “second Windows” on one physical PC)
Windows Sandbox is a great way to test with a **separate Windows instance** (separate clipboard, separate processes) while keeping the relay server on your host.

**Important:** Windows Sandbox **can share clipboard** with the host by default. To get a true cross-device style test, use a `.wsb` config that **disables clipboard redirection**.

1) Create a file like `Sandbox-ClipboardSync.wsb` anywhere (e.g. Desktop) with:

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

2) Start the relay server on the **host** bound to all interfaces:

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj -- --urls http://0.0.0.0:5104
```

3) Allow Windows Firewall inbound access for port **5104** (host).

4) Double-click the `.wsb` file to start Sandbox.

5) In Sandbox, open PowerShell and find the host IP:
- Run `ipconfig`
- The **Default Gateway** is typically the host/NAT gateway you can reach from Sandbox.

6) In Sandbox PowerShell, verify you can hit the host relay:

```powershell
Invoke-WebRequest http://<HOST_IP>:5104/ | Select-Object -ExpandProperty Content
```

7) Run the Windows agent in Sandbox.
Notes:
- Sandbox may not have the .NET 8 runtime/SDK. The simplest path is to **publish a self-contained** agent on the host and run the produced `.exe` from the mapped folder.
- Once the agent is running, open tray **Settings** and set **Server URL** to `http://<HOST_IP>:5104`.

8) Test:
- Copy text on host → paste in Sandbox (or vice versa).
- Because clipboard redirection is disabled, these are truly separate clipboards and the relay should sync them.

### Automated regression tests (recommended)

#### Protocol unit tests (fast)

```powershell
dotnet test .\tests\ClipboardSync.Protocol.Tests\ClipboardSync.Protocol.Tests.csproj -c Debug
```

Covers hashing, size limits, and loop guard behaviors.

#### Relay integration tests (SignalR + MessagePack)

```powershell
dotnet test .\tests\RelayServer.Tests\RelayServer.Tests.csproj -c Release
```

Why Release: if you have a Debug relay server running, it can lock Debug output files; running tests in Release avoids that.

Integration coverage:
- `ClipboardPublish` broadcasts `ClipboardChanged` to other clients
- New client connection receives the current “latest” clipboard value

### Run all tests

```powershell
dotnet test .\tests\ -c Release
```

## Current limitations (MVP)

- Text-only clipboard (no images/files)
- No authentication
- No encryption
- No persistence (server stores only “latest” in memory)

## How to keep this README up to date

As we add features (auth, encryption, Android client, persistence), update:
- **Major components** (new projects/folders)
- **Run** section (new args/settings)
- **Testing** section (new unit/integration/e2e tests)


