using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Config;

public class NamedAddress
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}

public class KaminoReserve
{
    public string Address { get; set; } = "";
    public string Symbol { get; set; } = "";
    public int Decimals { get; set; }
}

public class ExponentMarketConfig
{
    public string Name { get; set; } = "";
    public string Vault { get; set; } = "";
}

public class BotConfig
{
    public List<NamedAddress> KaminoMarkets { get; set; } = new();
    public List<KaminoReserve> KaminoReserves { get; set; } = new();
    public List<NamedAddress> LoopscaleVaults { get; set; } = new();

    public List<ExponentMarketConfig> ExponentMarkets { get; set; } = new();

    // شناسهٔ عددی تلگرام ادمین؛ فقط این کاربر می‌تواند /stats بزند. فقط از متغیر محیطی ADMIN_USER_ID خوانده می‌شود
    // (داخل فایل‌های ریپو نیست). 0 یعنی /stats غیرفعال است.
    public long AdminUserId { get; set; }

    // آدرس سرویس ExponentService؛ اگر خالی باشد پوزیشن Exponent نمایش داده نمی‌شود.
    public string ExponentServiceUrl { get; set; } = "";

    public static BotConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
        var config = JsonSerializer.Deserialize<BotConfig>(File.ReadAllText(path), Json.Options)!;

        if (long.TryParse(Environment.GetEnvironmentVariable("ADMIN_USER_ID"), out var adminId))
            config.AdminUserId = adminId;
        return config;
    }
}
