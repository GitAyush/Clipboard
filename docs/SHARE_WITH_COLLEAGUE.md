# Share ClipboardSync with a colleague (Windows)

This guide is for the setup where **each colleague signs in with their own Google account** and uses **their own Google Drive** for storage.

## What you send them
- The latest **WindowsAgent** zip (from GitHub Actions artifact: `WindowsAgent-win-x64`)
- The server URL:
  - `https://ec2-52-66-31-127.ap-south-1.compute.amazonaws.com`

## What they do (5 minutes)

### 1) Unzip and run
1. Unzip the folder anywhere (example: `C:\Apps\ClipboardSync\`)
2. Run `ClipboardSync.WindowsAgent.exe`
3. If Windows SmartScreen shows up: **More info → Run anyway**

### 2) Configure settings
Tray icon → **Settings**
- **Server URL**: `https://ec2-52-66-31-127.ap-south-1.compute.amazonaws.com`
- **Sync mode**: `Drive`
- **Use Google account for authentication**: ON
- **Room ID**: leave default (or set anything you like; it only affects your own devices)

### 3) Sign in to Google
A browser window opens:
1. Sign in with your Google account
2. Accept the requested permissions

### 4) Verify it works
To actually see syncing, you need **two devices**:
1. Repeat steps 1–3 on a second Windows device
2. Use the **same Google account** (and ideally the same Room ID)
3. Copy text on one device and confirm it appears on the other

## Troubleshooting
- If you signed into the wrong Google account: tray → **Google: sign out / switch account**.
- If corporate policies block sign-in or the server URL: try on a personal device/network, or contact your IT team.


