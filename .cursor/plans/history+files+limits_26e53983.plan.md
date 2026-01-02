---
name: History+Files+Limits
overview: Add remote clipboard history (last N) with UI access, configurable text/upload limits, and file support (auto small files + manual upload), supporting both Relay mode (room-scoped server storage + download) and Drive mode (Drive-backed storage + download), with tests after each step.
todos:
  - id: history-protocol
    content: Add shared DTOs for history items (text/file), history fetch, and download pointers.
    status: completed
  - id: relay-history-store
    content: "Relay mode: room-scoped history store (last N) + hub methods + HTTP download endpoint."
    status: completed
    dependencies:
      - history-protocol
  - id: drive-manifest-history
    content: "Drive mode: maintain a per-room manifest.json in appDataFolder for last N items and load it for UI."
    status: completed
    dependencies:
      - history-protocol
  - id: windows-history-ui
    content: Add tray recent submenu (last 5) + History window (10+) with Copy/Download actions.
    status: completed
    dependencies:
      - history-protocol
  - id: limits-config
    content: Add settings and enforcement for MaxInlineTextBytes (64/256KB) and MaxUploadBytes (1MB..10MB).
    status: completed
    dependencies:
      - windows-history-ui
  - id: file-support
    content: "Add file metadata + download flow: detect file clipboard, auto-handle <100KB, add Upload file… and max upload limit handling."
    status: completed
    dependencies:
      - limits-config
      - relay-history-store
      - drive-manifest-history
  - id: tests
    content: Add unit/integration tests for history, limits, and file flows; ensure coverage stays ≥70%.
    status: completed
    dependencies:
      - file-support
---

# Clipboard history + file support + configurable limits

## Goals

- **Remote history**: store last **N=10** items per room so users can re-copy any of them later.
- Relay mode: history stored on **server** (room-scoped).
- Drive mode: history stored on **Google Drive** (room-scoped).
- **Sync behavior stays the same**: only the **latest item** is pushed into the OS clipboard automatically.
- **Configurable limits**:
- Text inline sync: **64KB default**, optional **256KB**.
- “Upload as file” size: **1MB default**, configurable up to **10MB**.
- **Files**:
- Copying a small file (< **100KB**) can be auto-handled.
- Bigger files require explicit “Upload file…” action.
- Users can **download** files from history.
- **UX**:
- Tray shows last **5** history items (quick re-copy).
- A History window shows **10+** items with details and actions.

## Key decisions

- **History is remote**, not synced clipboard history.
- **Room scoping**: Relay mode will also use `roomId/roomSecret` (`JoinRoom`) so history is per-room.

## Data model (shared)

Add shared “history item” DTOs alongside existing text/pointer messages:

- `HistoryItem` (id, kind, ts, originDeviceId, title/preview, sizeBytes, contentHash, storagePointer)
- `HistoryList` (roomId, items[])
- `GetHistory(roomId, limit)` request/response message shapes (hub methods)
- For Relay mode downloads: item id maps to server endpoint `/download/{roomId}/{itemId}`

## Storage design

### Relay mode

- In-memory ring buffer per room (N=10 by default), later can move to DB.
- Store:
- text items inline
- file items as bytes (bounded by MaxUploadBytes)
- Provide:
- Hub method `GetHistory(limit)`
- Hub broadcast for latest item (existing) + history append
- HTTP GET download endpoint for file items

### Drive mode

- Keep payload in Drive (already), plus a **small manifest** in `appDataFolder`:
- `clips/<roomId>/manifest.json` containing last N pointers + metadata
- Clients update manifest on upload; clients read manifest to populate History UI.

## Client UI changes (Windows agent)

- Tray menu:
- “Recent (last 5)” submenu → clicking copies that item back to OS clipboard
- “Open History…” window
- “Upload file…”
- History window:
- list last N items
- actions:
- **Copy** (text)
- **Download** (file) to a chosen location
- Settings:
- `MaxInlineTextBytes` (64KB/256KB)
- `MaxUploadBytes` (1MB..10MB)

## Windows clipboard handling

- Detect file copy via `Clipboard.ContainsFileDropList()`.
- For auto file sync:
- if total size ≤ 100KB → upload/store immediately
- else → show “Upload file…” UI
- For large text:
- if text size > `MaxInlineTextBytes`, offer “Upload as file” (stored in history as file item)

## Testing strategy (high coverage)

- Unit tests:
- History ring buffer (append, dedupe, limit)
- Limit enforcement (64KB/256KB, upload 1MB..10MB)
- Drive manifest update logic (pure function)
- Relay mode: history serialization + download mapping
- Integration tests:
- Relay server: `GetHistory` returns last N, file download endpoint returns exact bytes
- Relay hub: room scoping + history events
- E2E (manual): host + Windows Sandbox for both modes

## Files to change/add

- Shared protocol:
- [`shared/ClipboardSync.Protocol/ClipboardMessages.cs`](shared/ClipboardSync.Protocol/ClipboardMessages.cs)
- Relay server:
- [`server/RelayServer/Hubs/ClipboardHub.cs`](server/RelayServer/Hubs/ClipboardHub.cs)
- Add new services: `InMemoryHistoryState`
- Add HTTP download endpoint in [`server/RelayServer/RelayServerApp.cs`](server/RelayServer/RelayServerApp.cs)
- Windows agent:
- Add History UI window + view model
- Add tray “Recent” submenu + “Upload file…” action
- Add clipboard file detection
- Add per-mode history fetch (Relay) / manifest read (Drive)

## Implementation todos

- **history-protocol**: Add shared DTOs for history items and history fetch.
- **relay-history-store**: Implement per-room history storage + hub methods + download endpoint.
- **drive-manifest-history**: Implement Drive manifest read/write for last N pointers.
- **windows-history-ui**: Add tray recent submenu + History window with Copy/Download.
- **limits-config**: Add settings for 64KB/256KB and 1MB..10MB; enforce across modes.
- **file-support**: Detect file clipboard, auto-handle <100KB, add “Upload file…” and download.
- **tests**: Add unit + integration tests for each step; keep coverage ≥70%.