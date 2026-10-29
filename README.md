# OnReBot

بات تلگرام برای نمایش رتبه و امتیاز OnRe و وضعیت پوزیشن‌های اکتیو (Kamino و Loopscale) یک ولت سولانا. امتیاز Exponent فعلاً فقط از API خود OnRe خوانده می‌شود.

## اجرا

1. از [@BotFather](https://t.me/BotFather) یک بات بساز و توکن را بگیر.
2. توکن را فقط به‌صورت متغیر محیطی `BOT_TOKEN` بده (در کد یا فایل ذخیره نمی‌شود):

PowerShell:

```powershell
$env:BOT_TOKEN = "123456:ABC..."
dotnet run --project OnReBot
```

bash:

```bash
export BOT_TOKEN="123456:ABC..."
dotnet run --project OnReBot
```

3. در تلگرام `/start` را بزن، دکمهٔ «🏆 رتبه من» را بزن و آدرس ولت را بفرست.

نیازمندی: .NET 8 SDK یا بالاتر.

## متغیرهای محیطی (هیچ‌کدام داخل ریپو نیستند)

| نام | توضیح |
|---|---|
| `BOT_TOKEN` | توکن بات از BotFather (اجباری) |
| `ADMIN_USER_ID` | شناسهٔ عددی تلگرام ادمین؛ فقط این کاربر می‌تواند `/stats` بزند (اختیاری) |
| `SOLANA_RPC_URL` | RPC سولانا برای سرویس Exponent (اختیاری) |

برای اجرای محلی می‌توانی این‌ها را در یک فایل `run-local.ps1` بگذاری؛ این نام در `.gitignore` هست.

## تنظیمات (`OnReBot/Config/appsettings.json`)

| کلید | توضیح |
|---|---|
| `KaminoMarkets` | آرایه‌ای از `{Name, Address}`. برای افزودن مارکت جدید یک مورد اضافه کن. فقط OnRe Market پیش‌فرض فعال است (PT-ONyc امتیاز نمی‌گیرد). |
| `KaminoReserves` | reserveهای شناخته‌شده `{Address, Symbol, Decimals}`؛ مقدار توکن فقط برای این reserveها نمایش داده می‌شود. |
| `LoopscaleVaults` | آرایه‌ای از `{Name, Address}` برای vaultهای Lend. |
| `ExponentMarkets` | نگاشت مارکت ↔ vault برای فاز ۲ (فعلاً استفاده نمی‌شود). |

## رفتار

- آدرس قبل از هر درخواست اعتبارسنجی می‌شود (base58، طول ۳۲ تا ۴۴).
- حداکثر ۲ گزارش برای هر کاربر در هر ۵ دقیقه. هیچ کشی وجود ندارد و هر گزارش داده‌ی تازه می‌خواند.
- خرابی یک پلتفرم فقط همان بخش را «در حال حاضر در دسترس نیست» می‌کند.
- Exponent: هیچ تلاشی برای عبور از Vercel Security Checkpoint نشده است. فاز ۲ از طریق `IExponentPositionProvider` اضافه می‌شود.

## تست

```bash
dotnet build
dotnet test
```

داده‌های نمونه در `OnReBot.Tests/TestData` هستند. تست‌ها به API زنده وصل نمی‌شوند.

## پوزیشن Exponent (فاز ۲)

پوزیشن YT از طریق یک سرویس Node/TS کوچک (`ExponentService/`) و `@exponent-labs/exponent-sdk` خوانده می‌شود. هیچ دور زدن Vercel در کار نیست؛ سرویس مستقیم از RPC سولانا می‌خواند.

```bash
cd ExponentService
npm install
npm start
```

- آدرس سرویس در `ExponentServiceUrl` (فایل appsettings) است؛ اگر خالی بگذاری پوزیشن Exponent نمایش داده نمی‌شود.
- RPC پیش‌فرض عمومی است و rate-limit دارد. برای استفادهٔ جدی `SOLANA_RPC_URL` را روی یک RPC خصوصی بگذار. پورت با `EXPONENT_SERVICE_PORT` عوض می‌شود (پیش‌فرض ۳۰۰۱).
- فعلاً فقط پوزیشن YT نمایش داده می‌شود (مقدار YT، سررسید، سود قابل برداشت). پوزیشن LP پیاده‌سازی نشده است.
- ترتیب اجرا: اول سرویس Node، بعد بات.
