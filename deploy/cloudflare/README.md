# Cloudflare Tunnel hosting (temporary, always-on if your PC stays on)

Use this while Oracle VM setup is blocked. This exposes your local RelayServer publicly over HTTPS **without opening ports**.

## What you run (on your Windows host)
- RelayServer on `http://127.0.0.1:5104`
- `cloudflared` tunnel that forwards public HTTPS → `http://127.0.0.1:5104`

## Option A (fastest): ephemeral `trycloudflare.com` URL
No Cloudflare account or domain required. URL changes each time you start it.

### 1) Start RelayServer

```powershell
dotnet run --project .\server\RelayServer\RelayServer.csproj -c Release -- --urls http://127.0.0.1:5104
```

### 2) Start tunnel

Install `cloudflared` (Windows) and then run:

```powershell
cloudflared tunnel --url http://127.0.0.1:5104
```

It will print a public URL like `https://something.trycloudflare.com`.

### 3) Verify
- `https://something.trycloudflare.com/healthz`
- `https://something.trycloudflare.com/auth/status`

Use that base URL in the Windows agent **Server URL**.

## Option B (stable): named tunnel + your own domain (recommended)
Requires a Cloudflare account + a domain added to Cloudflare DNS.

High-level steps:
1. `cloudflared tunnel login`
2. `cloudflared tunnel create clipboardsync`
3. `cloudflared tunnel route dns clipboardsync clip.yourdomain.com`
4. Run tunnel as a service using a config file.

### Example config file (Windows)
Create `C:\ClipboardSync\cloudflared.yml`:

```yaml
tunnel: clipboardsync
credentials-file: C:\\Users\\<you>\\.cloudflared\\<tunnel-id>.json

ingress:
  - hostname: clip.yourdomain.com
    service: http://127.0.0.1:5104
  - service: http_status:404
```

Run:

```powershell
cloudflared tunnel --config C:\ClipboardSync\cloudflared.yml run
```

## Notes
- Cloudflare Tunnel supports websockets; SignalR should work fine.
- “Always-on” depends on your host PC staying online. If you want, we can add a Windows Scheduled Task to auto-start both RelayServer and `cloudflared` on boot.


