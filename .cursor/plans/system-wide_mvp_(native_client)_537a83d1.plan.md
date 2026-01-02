---
name: System-Wide MVP (Native Client)
overview: Keep the existing SignalR+MessagePack relay and shared protocol; add a native system-wide clipboard agent on Windows (background/tray) to sync text clipboard in real time. Structure the client so platform-specific clipboard access is isolated for future cross-platform expansion.
todos:
  - id: client-scaffold
    content: Create `clients/ClipboardSync.WindowsAgent` project (WPF tray) and reference `shared/ClipboardSync.Protocol`. Add settings persistence (server URL + deviceId).
    status: completed
  - id: client-signalr
    content: Implement SignalR MessagePack connection to `/hub/clipboard`, subscribe to `ClipboardChanged`, and handle reconnect.
    status: completed
    dependencies:
      - client-scaffold
  - id: client-clipboard-watch
    content: Implement system clipboard watcher (text-only) and publish `ClipboardPublish` when local clipboard changes.
    status: completed
    dependencies:
      - client-scaffold
      - client-signalr
  - id: client-loop-prevention
    content: Apply `ClipboardLoopGuard` rules to prevent feedback loops and reduce churn (ignore own origin, ignore window after remote set, debounce, suppress duplicates).
    status: completed
    dependencies:
      - client-clipboard-watch
  - id: client-basic-ui
    content: Add tray icon menu (connect/disconnect, settings, quit) and simple status/log view for testing.
    status: completed
    dependencies:
      - client-signalr
---

# System-Wide Clipboard Sync MVP (Windows Native Agent)

## Decision

- **System-wide MVP only**: we will build a **native Windows background client** that can read/write the OS clipboard globally (all apps), connect to the existing relay server, and apply loop prevention.
- Chrome extension is **not** part of the system-wide MVP (it cannot reliably monitor OS clipboard continuously without a native host).

## Current state (already done)

- **Shared protocol**: [`shared/ClipboardSync.Protocol`](shared/ClipboardSync.Protocol) with MessagePack DTOs + hashing + loop guard.
- **Relay server**: [`server/RelayServer`](server/RelayServer) SignalR hub `/hub/clipboard` with MessagePack, in-memory latest state, broadcast on publish.

## Client approach (recommended)

- **Windows clipboard agent**: .NET 8 background app with optional tray UI.
- Keep clipboard access behind an interface so later we can add Android/macOS/Linux implementations without changing sync logic.

### Client layering

- **Core sync logic (portable)**: connects to SignalR, publishes local changes, applies remote changes using `ClipboardLoopGuard`.
- **Platform clipboard adapter (Windows-only for MVP)**: reads clipboard changes and sets clipboard text.

## Directory layout (updated)

- [`clients/ClipboardSync.ClientCore`](clients/ClipboardSync.ClientCore) (optional class library): reusable sync logic + abstractions
- [`clients/ClipboardSync.WindowsAgent`](clients/ClipboardSync.WindowsAgent) (Windows app): clipboard adapter + tray/settings

If you want to keep it simpler initially, we can skip `ClientCore` and place everything in the Windows project, but still keep code separated into `Clipboard/*` and `Sync/*` folders.

## Windows clipboard implementation options

- **Option A (easiest, reliable)**: WPF tray app (runs STA thread; clipboard APIs are simplest).
- **Option B (no UI)**: .NET Generic Host + hidden Win32 message window to receive clipboard update notifications.

**Recommendation for MVP**: Option A (WPF tray) because it’s faster to implement and test, and we can keep UI minimal.

## Phase 1 Client Features (what we’ll implement next)

- **Connect** to relay server `/hub/clipboard` using SignalR MessagePack.
- **Watch** clipboard for text changes (polling or Win events).
- **Publish** text to server as `ClipboardPublish` with `deviceId`, `clientItemId`, `ts`, `textHash`.
- **Receive** `ClipboardChanged` and apply to clipboard.
- **Loop prevention** using `ClipboardLoopGuard`:
- Ignore own `originDeviceId`.
- Ignore local clipboard event for a short window after remote apply.
- Debounce rapid local changes.
- Suppress duplicate publishes by hash.
- **Basic settings**: server URL + device name (deviceId persisted locally).

## Testing plan (system-wide)

### Server

- Run relay:
- `dotnet run --project server/RelayServer/RelayServer.csproj`
- Verify:
- `http://localhost:5104/` returns the status text.

### Client

- Run **two instances** of the Windows agent (on the same PC or two PCs).
- Copy text in any application (Notepad/VS Code/Browser):
- Expect near-real-time update on the other instance.
- Verify loop prevention:
- Remote apply should not cause an infinite “ping-pong” of updates.
- Verify convergence:
- Launch a new client while another is connected; it should immediately receive the latest clipboard value (server pushes it on connect).

## Key files we’ll add (next)

- `clients/ClipboardSync.WindowsAgent/App.xaml` + `App.xaml.cs`
- `clients/ClipboardSync.WindowsAgent/Tray/TrayIcon.cs` (or similar)
- `clients/ClipboardSync.WindowsAgent/Clipboard/WindowsClipboardWatcher.cs`
- `clients/ClipboardSync.WindowsAgent/Sync/RelayConnection.cs`
- `clients/ClipboardSync.WindowsAgent/Settings/SettingsStore.cs` (server URL, deviceId)

## Implementation todos

- **client-scaffold**: Create Windows agent project and reference `ClipboardSync.Protocol`.
- **client-signalr**: Implement SignalR MessagePack client connect/reconnect + handler for `ClipboardChanged`.
- **client-clipboard-watch**: Implement system clipboard watcher (text-only) and publish `ClipboardPublish`.
- **client-loop-prevention**: Wire `ClipboardLoopGuard` for ignore/debounce/duplicate suppression.
- **client-basic-ui**: Add tray + minimal settings (server URL) and status (connected/disconnected).