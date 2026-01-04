# RelayServer deployment (Linux VM with Docker) — Oracle / AWS / any VPS

This folder contains a Docker-based deployment for `server/RelayServer`:
- `relayserver` container (ASP.NET Core)
- `caddy` container (HTTPS + automatic Let’s Encrypt)

## Prereqs (on the VM)
- Docker Engine
- Docker Compose plugin (`docker compose`)
- Open inbound ports (provider firewall / security group):
  - **22/tcp** (SSH)
  - **80/tcp** and **443/tcp** (HTTPS via Caddy) — recommended
  - Optional: **5104/tcp** (HTTP-only fallback)

## DNS (required for Let’s Encrypt HTTPS)
You need a DNS name that points to the VM public IP.

If you don't own a domain, a good temporary option is **DuckDNS** (free subdomain):
- Example: `yourname.duckdns.org`
- Create it at `https://www.duckdns.org/`
- Use an **Elastic IP** (AWS) or a stable VM IP, then run the DuckDNS update once:

```bash
curl "https://www.duckdns.org/update?domains=YOUR_SUBDOMAIN&token=YOUR_TOKEN&ip=YOUR_VM_PUBLIC_IP"
```

If you do not have a domain yet, you can run **HTTP-only** temporarily by exposing port 5104 directly (not recommended long-term).

## Bring up (HTTPS, recommended)

From repo root on the VM (or after copying `deploy/relayserver` onto the VM):

```bash
cd deploy/relayserver

export DOMAIN="clip.yourdomain.com"
export ACME_EMAIL="you@example.com"

docker compose up -d --build
```

Verify:
- `https://$DOMAIN/healthz`
- `https://$DOMAIN/auth/status`

## Bring up (HTTP-only, no domain)
If you have no DNS name yet, run RelayServer without Caddy (plain HTTP on port 5104):

```bash
cd deploy/relayserver
docker compose -f docker-compose.http.yml up -d --build
```

Then verify:
- `http://YOUR_VM_PUBLIC_IP:5104/healthz`

## Server auth config (production)
Set these as environment variables on the VM (recommended):
- `Auth__Enabled=true`
- `Auth__JwtSigningKey=<long random string>`
- `Auth__GoogleClientIds__0=<your Google OAuth desktop client_id>`

Then restart:

```bash
docker compose restart relayserver
```

## AWS EC2 Free Tier quick start (Ubuntu)
This is a good temporary replacement while Oracle signup is pending.

### 1) Create an EC2 instance
- Choose **Ubuntu 22.04 LTS**
- Instance type: **t2.micro** (Free Tier)
- Storage: default is fine
- Create/download an SSH keypair (`.pem`)
- Security Group inbound:
  - `22/tcp` from your IP
  - `80/tcp` from `0.0.0.0/0`
  - `443/tcp` from `0.0.0.0/0`
  - Optional: `5104/tcp` from your IP (HTTP-only)

Highly recommended:
- Allocate and associate an **Elastic IP** (keeps the public IP stable)

### 2) SSH in and install Docker
On the VM:

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER
```

Log out and back in (so `docker` works without sudo).

### 3) Copy repo to the VM and start
You can either:
- `git clone` the repo on the VM, or
- use your existing GitHub Actions workflow (recommended)


