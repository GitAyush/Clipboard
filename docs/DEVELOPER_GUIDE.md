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

## Deployment (Linux VM + Docker) — Oracle / AWS / any VPS

This repo includes a Docker-based deployment bundle:
- `deploy/relayserver/` (RelayServer + Caddy HTTPS)

You can run it on:
- Oracle Always Free (Ubuntu)
- AWS EC2 Free Tier (Ubuntu)
- Any small VPS (Hetzner/DigitalOcean/etc.)

### CI/CD secrets (GitHub Actions)
The workflow `.github/workflows/relayserver-deploy.yml` was initially written for Oracle, but the **same secrets work for any Ubuntu VM** (the names are historical):
- `ORACLE_HOST`: VM public IP or DNS name
- `ORACLE_USER`: SSH username (commonly `ubuntu` on AWS/Oracle Ubuntu images)
- `ORACLE_SSH_PRIVATE_KEY`: private key used for SSH deploy
- `ORACLE_APP_DIR`: remote folder to sync into (e.g. `/opt/clipboardsync`)
- `RELAY_DOMAIN`: DNS name for HTTPS (can be a free DuckDNS name)
- `ACME_EMAIL`: email for Let’s Encrypt registration
- `AUTH_ENABLED`: `true` or `false`
- `AUTH_JWT_SIGNING_KEY`: long random string (server-only secret)
- `AUTH_GOOGLE_CLIENT_ID`: your Google OAuth Desktop client_id

See `deploy/relayserver/README.md` for AWS EC2 steps + no-domain fallback.

## Deployment (temporary): Cloudflare Tunnel

If Oracle account provisioning is blocked, you can expose a local RelayServer over HTTPS using Cloudflare Tunnel.

See: `deploy/cloudflare/README.md`.


