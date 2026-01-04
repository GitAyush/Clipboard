# ClipboardSync — User guide

ClipboardSync syncs your clipboard across devices by using:
- A small **RelayServer** (SignalR) for real-time notifications
- A Windows tray app (**WindowsAgent**) that reads/writes your clipboard

It supports two sync modes:
- **Relay mode**: server relays clipboard payload (text, and optional file upload via server)
- **Drive mode**: clipboard payload is stored in **your Google Drive** (`appDataFolder`); server relays **pointers only**

## Quick start (most users)

### 1) Start the server

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj -c Release -- --urls http://0.0.0.0:5104
```

If Windows Firewall prompts, allow inbound connections for port **5104**.

### 2) Start the Windows agent

```powershell
.\clients\ClipboardSync.WindowsAgent\bin\Release\net8.0-windows\ClipboardSync.WindowsAgent.exe
```

Tray → **Settings**:
- **Server URL**: `http://localhost:5104` (same machine) or `http://<HOST_IP>:5104` (another machine / Sandbox)
- Choose **Sync mode**: `Relay` or `Drive`

## Using Relay mode

Relay mode is the simplest: the server receives clipboard text and broadcasts it.

See `README.relay.md` for details.

## Using Drive mode

Drive mode never uploads clipboard payloads to the RelayServer. Payloads are stored in your Google Drive.

See `README.drive.md` for details (OAuth setup, Sandbox testing, etc.).

## Google sign-in (optional, recommended for “same account sync”)

If you enable Google-based sharing:
- Devices signed into the **same Google account** will sync together
- Rooms remain useful for testing/segmentation (within an account)

This requires enabling server auth and turning on “Use Google account for authentication” in the agent.

### What data is used and where it’s stored (transparency)
- **Google sign-in**: the agent uses Google OAuth to get permission to use **your Google Drive appDataFolder** (Drive mode) and (optionally) to authenticate to the RelayServer (Google-auth mode).
- **Stored on your PC**: Google OAuth tokens (so you don’t have to log in every time). You can clear them via “Google: sign out / switch account”.
- **Stored in your Google Drive** (Drive mode): clipboard payloads (text / file data) and a small manifest in `appDataFolder`.
- **Stored on the RelayServer**:
  - In Relay mode: clipboard payloads are relayed through the server.
  - In Drive mode: the server relays **pointers** only.
  - In Google-auth mode: the server validates your Google token and issues a short-lived RelayServer JWT; the server uses your Google account subject to isolate rooms per account.

## Troubleshooting

### “RelayServer auth is DISABLED”
- Your server is running with `Auth:Enabled=false` (or you’re running an older server build).
- Fix: enable auth in your local server config (see `README.drive.md`) and restart the server.

### Sandbox can’t reach server
- Don’t use `localhost` inside Sandbox.
- Bind server to `0.0.0.0` and use `http://<HOST_IP>:5104` from Sandbox.

### Hosted server (Oracle VM)
If your server is hosted on a VM:
- Prefer **HTTPS** with a domain name (required for Let’s Encrypt).
- Your server URL in the agent should be: `https://<your-domain>`

### Hosted server (AWS / any VPS)
The same VM guidance applies to AWS EC2 or any VPS:
- You can use a real domain, a free DNS name (DuckDNS), or the EC2 **Public IPv4 DNS** name.
- If a corporate network blocks DuckDNS, prefer the EC2 hostname:
  - `https://ec2-<ip>.<region>.compute.amazonaws.com`
See `deploy/relayserver/README.md` for the Docker+Caddy VM steps.

### Hosted server (temporary): Cloudflare Tunnel from your PC
If Oracle VM setup is blocked, you can expose a local RelayServer over HTTPS with Cloudflare Tunnel (no port forwarding).

See: `deploy/cloudflare/README.md`.

## Monitoring (recommended for hosted servers)
If you host RelayServer on a VM (AWS/Oracle/etc.), use a free uptime monitor (e.g. UptimeRobot) to hit:
- `https://<your-domain>/healthz`



