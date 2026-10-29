# استقرار روی Ubuntu

ساختار روی سرور:

```
/opt/onrebot/
├── bot/        ← خروجی dotnet publish (بات .NET)
└── exponent/   ← سرویس Node (dist + package.json + node_modules)
/etc/onrebot.env        ← توکن و تنظیمات (فقط روی سرور، هرگز داخل ریپو)
/var/lib/onrebot/       ← فایل کاربران (users.txt) — خودکار ساخته می‌شود
```

سرویس Exponent فقط روی `127.0.0.1:3001` گوش می‌دهد، پس لازم نیست پورتی باز کنی.

## ۱) روی سیستم خودت: ساخت فایل‌ها

```bash
# بات
dotnet publish OnReBot -c Release -o publish-bot -p:UseAppHost=false

# سرویس Exponent
cd ExponentService
npm ci
npm run build
```

برای آپلود (مثلاً با FileZilla و SFTP) این‌ها را آماده کن:

- پوشهٔ `publish-bot/` ← کل محتوا
- از `ExponentService/`: فقط `dist/`، `package.json` و `package-lock.json` (بدون `node_modules`)
- از `deploy/`: فایل‌های `*.service` و `onrebot.env.example`

**فایل `run-local.ps1` را هرگز نبر.**

## ۲) روی سرور: آماده‌سازی (یک بار)

```bash
sudo apt update
sudo apt install -y dotnet-runtime-8.0
```

اگر پکیج `dotnet-runtime-8.0` پیدا نشد، runtime را طبق راهنمای رسمی مایکروسافت برای Ubuntu نصب کن.

Node.js (نسخهٔ apt اوبونتو قدیمی است؛ با NodeSource نصب کن):

```bash
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt install -y nodejs
node -v
```

> از Node 24 استفاده کن (npm 11): فایل `package-lock.json` با همین نسخه ساخته و تست شده. با Node 22 (npm 10) دستور `npm ci` خطای `Missing: utf-8-validate from lock file` می‌دهد. اگر مجبور به نسخهٔ پایین‌تر شدی، به‌جای `npm ci` از `npm install --omit=dev` استفاده کن.

Swap یک گیگابایتی (برای سرور ۷۶۸ مگابایتی مهم است):

```bash
sudo fallocate -l 1G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
free -h
```

کاربر سیستمی و پوشه‌ها:

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin onrebot
sudo mkdir -p /opt/onrebot/bot /opt/onrebot/exponent
```

## ۳) روی سرور: گذاشتن فایل‌ها

فرض کن فایل‌ها را در `~/upload/` (با پوشه‌های `bot` و `exponent` و فایل‌های `.service` و `onrebot.env.example`) آپلود کرده‌ای:

```bash
sudo cp -r ~/upload/bot/. /opt/onrebot/bot/
sudo cp -r ~/upload/exponent/. /opt/onrebot/exponent/
cd /opt/onrebot/exponent && sudo npm ci --omit=dev
sudo chown -R onrebot:onrebot /opt/onrebot
```

اگر هنگام `npm ci` خطای ساخت (node-gyp) دیدی، `sudo apt install -y build-essential python3` را بزن و دوباره اجرا کن. این وابستگی‌ها اختیاری‌اند و بدون آن‌ها هم سرویس کار می‌کند.

## ۴) تنظیم توکن

```bash
sudo cp ~/upload/onrebot.env.example /etc/onrebot.env
sudo nano /etc/onrebot.env        # BOT_TOKEN و ADMIN_USER_ID را پر کن
sudo chmod 600 /etc/onrebot.env
```

## ۵) راه‌اندازی سرویس‌ها

```bash
sudo cp ~/upload/onrebot-*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now onrebot-exponent onrebot-bot
```

## ۶) بررسی

```bash
systemctl status onrebot-exponent onrebot-bot
journalctl -u onrebot-bot -f              # لاگ زندهٔ بات (Ctrl+C برای خروج)
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3001/x    # باید 404 بدهد = سرویس بالاست
curl -sI https://api.telegram.org | head -1                         # دسترسی به تلگرام
free -h                                                             # مصرف رم
```

در لاگ بات باید `Bot @... is running` ببینی.

## به‌روزرسانی

1. روی سیستمت دوباره publish و build کن (مرحلهٔ ۱).
2. فایل‌های جدید را روی `/opt/onrebot/...` بریز (مرحلهٔ ۳؛ اگر `package-lock.json` عوض شده، دوباره `npm ci --omit=dev`).
3. `sudo chown -R onrebot:onrebot /opt/onrebot`
4. `sudo systemctl restart onrebot-exponent onrebot-bot`

## نکته‌ها

- فایل کاربران در `/var/lib/onrebot/OnReBot/users.txt` است و با به‌روزرسانی پاک نمی‌شود.
- اگر `ADMIN_USER_ID` ست نشود، `/stats` برای هیچ‌کس جواب نمی‌دهد.
- RPC عمومی سولانا کند است. برای گزارش سریع‌تر Exponent، یک RPC خصوصی بگیر و `SOLANA_RPC_URL` را در `/etc/onrebot.env` بگذار، بعد `sudo systemctl restart onrebot-exponent`.
- سقف حافظه در فایل‌های systemd (`MemoryMax`) برای سرور ۷۶۸ مگابایتی تنظیم شده؛ اگر سرویس پشت‌سرهم ری‌استارت می‌شد، `journalctl` را برای پیام کمبود حافظه ببین.
