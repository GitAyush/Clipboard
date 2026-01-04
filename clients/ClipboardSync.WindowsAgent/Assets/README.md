# WindowsAgent icon (optional)

If you want a custom icon for the WindowsAgent `.exe` and tray icon:

1) Create an `.ico` file (recommended sizes: 16, 32, 48, 256).
2) Save it as:
- `clients/ClipboardSync.WindowsAgent/Assets/ClipboardSync.ico`

Then rebuild/publish. The app will:
- embed it into the published `ClipboardSync.WindowsAgent.exe` (MSBuild `ApplicationIcon`)
- use it for the tray icon automatically

Note: this repo does not include a default `.ico` binary.


