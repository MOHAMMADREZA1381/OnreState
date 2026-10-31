# Deploying on Ubuntu

Layout on the server:

```
/opt/onrebot/
├── bot/        ← output of dotnet publish (the .NET bot)
└── exponent/   ← the Node service (dist + package.json + node_modules)
/etc/onrebot.env        ← token and settings (only on the server, never in the repository)
/var/lib/onrebot/       ← user list (users.txt), created automatically
```

The Exponent service only listens on `127.0.0.1:3001`, so no port needs to be opened.

## 1) On your machine: build the files

```bash
# bot
dotnet publish OnReBot -c Release -o publish-bot -p:UseAppHost=false

# Exponent service
cd ExponentService
npm ci
npm run build
```

Prepare these for upload (for example with FileZilla over SFTP):

- the `publish-bot/` folder: everything inside it
- from `ExponentService/`: only `dist/`, `package.json` and `package-lock.json` (no `node_modules`)
- from `deploy/`: the `*.service` files and `onrebot.env.example`

**Never upload `run-local.ps1`.**

## 2) On the server: one-time setup

```bash
sudo apt update
sudo apt install -y dotnet-runtime-8.0
```

If the `dotnet-runtime-8.0` package is not found, install the runtime following Microsoft's official instructions for Ubuntu.

Node.js (Ubuntu's apt version is old, so use NodeSource):

```bash
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt install -y nodejs
node -v
```

> Use Node 24 (npm 11): `package-lock.json` was created and tested with it. With Node 22 (npm 10), `npm ci` fails with `Missing: utf-8-validate from lock file`. If you have to use an older version, run `npm install --omit=dev` instead of `npm ci`.

A 1 GB swap file (important on a 768 MB server):

```bash
sudo fallocate -l 1G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
free -h
```

The service user and folders:

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin onrebot
sudo mkdir -p /opt/onrebot/bot /opt/onrebot/exponent
```

## 3) On the server: put the files in place

Assume you uploaded the files to `~/upload/` (with the `bot` and `exponent` folders, the `.service` files and `onrebot.env.example`):

```bash
sudo cp -r ~/upload/bot/. /opt/onrebot/bot/
sudo cp -r ~/upload/exponent/. /opt/onrebot/exponent/
cd /opt/onrebot/exponent && sudo npm ci --omit=dev
sudo chown -R onrebot:onrebot /opt/onrebot
```

If `npm ci` shows a build error (node-gyp), run `sudo apt install -y build-essential python3` and try again. Those dependencies are optional and the service works without them.

## 4) Configure the token

```bash
sudo cp ~/upload/onrebot.env.example /etc/onrebot.env
sudo nano /etc/onrebot.env        # fill in BOT_TOKEN and ADMIN_USER_ID
sudo chmod 600 /etc/onrebot.env
```

## 5) Start the services

```bash
sudo cp ~/upload/onrebot-*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now onrebot-exponent onrebot-bot
```

## 6) Check that it works

```bash
systemctl status onrebot-exponent onrebot-bot
journalctl -u onrebot-bot -f              # live bot log (Ctrl+C to leave)
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3001/x    # should print 404 = the service is up
curl -sI https://api.telegram.org | head -1                         # Telegram is reachable
free -h                                                             # memory use
```

The bot log should contain `Bot @... is running`.

## Updating (one command)

Once the server is set up as above, every time the code changes just run this in PowerShell from the project root:

```powershell
.\deploy\deploy.ps1 -Server root@YOUR_SERVER_IP
```

If PowerShell refuses to run the script (execution policy), run it for this one time like this instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\deploy.ps1 -Server root@YOUR_SERVER_IP
```

The script builds the bot and the Exponent service, uploads everything over a single SSH connection (the password is asked only once), reinstalls the Node dependencies only when `package-lock.json` changed, and restarts the services. At the end it prints the `active` status and a few lines of the bot log.

- Add `-DryRun` to see what would be uploaded without changing anything.
- No password or token is stored in the script; the token stays in `/etc/onrebot.env` on the server.
- Files that were deleted from the code are not removed from the server (new files only overwrite old ones).

Manual alternative: publish and build, copy the files to `/opt/onrebot/...`, run `sudo chown -R onrebot:onrebot /opt/onrebot`, then `sudo systemctl restart onrebot-exponent onrebot-bot`.

## Notes

- The user list is `/var/lib/onrebot/OnReBot/users.txt` and is not removed by updates.
- If `ADMIN_USER_ID` is not set, nobody gets a reply to `/stats`.
- The public Solana RPC is slow and sometimes unreliable. For faster Exponent reports, get a private RPC, put it in `/etc/onrebot.env` as `SOLANA_RPC_URL`, then run `sudo systemctl restart onrebot-exponent`.
- The `MemoryMax` limits in the systemd files are tuned for a 768 MB server; if a service keeps restarting, check `journalctl` for out-of-memory messages.
