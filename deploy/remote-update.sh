#!/usr/bin/env bash
# Runs on the server after deploy.ps1 has extracted the new files into /opt/onrebot.
set -euo pipefail

cd /opt/onrebot/exponent

# Install Node dependencies only when package-lock.json changed (npm ci is heavy on a small server).
new=$(sha256sum package-lock.json | cut -d' ' -f1)
old=$(cat .lock.sha 2>/dev/null || true)
if [ "$new" != "$old" ] || [ ! -d node_modules ]; then
  echo "Installing Node dependencies..."
  npm ci --omit=dev
  echo "$new" > .lock.sha
fi

chown -R onrebot:onrebot /opt/onrebot

systemctl restart onrebot-exponent onrebot-bot
sleep 8
echo "--- service status:"
systemctl is-active onrebot-exponent onrebot-bot
echo "--- last bot log lines:"
journalctl -u onrebot-bot -n 3 --no-pager
