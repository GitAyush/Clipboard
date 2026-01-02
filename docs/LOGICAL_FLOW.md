# ClipboardSync — Logical flow (pictorial)

This document shows the **high-level flow** of ClipboardSync in both variants.

## Common client loop (Windows agent)

```mermaid
flowchart TD
  clipboard[WindowsClipboard] --> watcher[ClipboardWatcherOrPoller]
  watcher -->|TextChanged| loopGuard[ClipboardLoopGuard]
  loopGuard --> decision{PublishEnabled?}
  decision -->|No| stop1[NoOp]
  decision -->|Yes| mode{SyncMode}
  mode -->|Relay| relayPublish[SignalR: ClipboardPublish(Text)]
  mode -->|Drive| driveUpload[DriveUpload(Text)]
  driveUpload --> pointerPublish[SignalR: ClipboardPointerPublish(Pointer)]

  relayRecv[SignalR: ClipboardChanged] --> apply1[ApplyToClipboard]
  pointerRecv[SignalR: ClipboardPointerChanged] --> driveDownload[DriveDownload(FileId)]
  driveDownload --> apply2[ApplyToClipboard]
```

## Relay mode (server relays payload)

```mermaid
sequenceDiagram
participant WinA as WindowsAgentA
participant Relay as RelayServer
participant WinB as WindowsAgentB

WinA->>Relay: Connect /hub/clipboard
WinB->>Relay: Connect /hub/clipboard

WinA->>Relay: ClipboardPublish(text,hash,deviceId)
Relay-->>WinA: ClipboardChanged(originDeviceId,text,hash)
Relay-->>WinB: ClipboardChanged(originDeviceId,text,hash)

WinB-->>WinB: ApplyToClipboard(loopGuard)
```

## Drive mode (server relays pointers only)

```mermaid
sequenceDiagram
participant WinA as WindowsAgentA
participant Drive as GoogleDrive(appDataFolder)
participant Notify as RelayServer(pointerOnly)
participant WinB as WindowsAgentB

WinA->>Notify: Connect /hub/clipboard
WinB->>Notify: Connect /hub/clipboard
WinA->>Notify: JoinRoom(roomId,roomSecret)
WinB->>Notify: JoinRoom(roomId,roomSecret)

WinA->>Drive: UploadText(objectKey,text)
WinA->>Notify: ClipboardPointerPublish(pointer(fileId,objectKey,hash,size))
Notify-->>WinB: ClipboardPointerChanged(pointer)
WinB->>Drive: DownloadText(fileId)
WinB-->>WinB: ApplyToClipboard(loopGuard)
```

## Server-side scoping (rooms)

```mermaid
flowchart TD
  conn[SignalRConnection] --> join[JoinRoom(roomId,secret)]
  join --> validate[ValidateOrCreateRoom(secretHash)]
  validate --> group[AddToSignalRGroup(roomId)]
  group --> latest[SendLatestPointerToCallerIfAny]
  publish[ClipboardPointerPublish(pointer)] --> check[EnsureJoinedRoomAndRoomMatch]
  check --> store[UpdateLatestPointer(roomId)]
  store --> broadcast[BroadcastToGroup(roomId)]
```


