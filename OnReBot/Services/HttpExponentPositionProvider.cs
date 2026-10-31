using System.Globalization;
using System.Text.Json;
using OnReBot.Config;
using OnReBot.Models;

namespace OnReBot.Services;

// پوزیشن‌های Exponent (Farm، Protected، Liquidity) را از سرویس Node (ExponentService) می‌خواند.
public class HttpExponentPositionProvider(HttpClient http, BotConfig config) : IExponentPositionProvider
{
    public async Task<ExponentResult> GetPositionsAsync(string wallet, CancellationToken ct)
    {
        var vaults = string.Join("&", config.ExponentMarkets.Select(m => $"vault={m.Vault}"));
        var url = $"{config.ExponentServiceUrl.TrimEnd('/')}/positions/{wallet}?{vaults}";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct), config.ExponentMarkets);
    }

    public static ExponentResult Parse(string json, List<ExponentMarketConfig> markets)
    {
        var result = JsonSerializer.Deserialize<ExponentServiceResponse>(json, Json.Options) ?? new();
        var positions = new List<ExponentPosition>();

        foreach (var p in result.Positions)
        {
            // نام از config (اگر باشد)، وگرنه نامی که سرویس از روی مارکت ساخته، وگرنه آدرس vault
            var name = markets.FirstOrDefault(m => m.Vault == p.Vault)?.Name ?? p.Name ?? p.Vault;
            var details = $"YT balance: {Num(p.Yt)}"
                + $"\nMaturity: {p.ExpiresAt.UtcDateTime:yyyy-MM-dd}"
                + $"\nClaimable interest: {Num(p.ClaimableInterest)}";
            if (p.CurrentValue != null)
                details += $"\nCurrent value: ${Num(p.CurrentValue.Value)}";
            if (p.AvgCostBasis != null)
                details += $"\nAvg cost per YT: ${p.AvgCostBasis.Value.ToString("0.0000", CultureInfo.InvariantCulture)}";

            // همان تفکیک اپ Exponent: Total PnL = Price appreciation + Claimed yield + Realized PnL (+ Unclaimed yield)
            if (p.TotalPnl != null)
            {
                details += $"\nTotal PnL: {Signed(p.TotalPnl.Value)}";
                if (p.PriceAppreciation != null)
                    details += $"\n  ◦ Price appreciation: {Signed(p.PriceAppreciation.Value)}";
                if (p.ClaimedInterestUsd != null)
                    details += $"\n  ◦ Claimed yield: {Signed(p.ClaimedInterestUsd.Value)}";
                if (p.RealizedPnl != null)
                    details += $"\n  ◦ Realized PnL: {Signed(p.RealizedPnl.Value)}";
                if (p.UnclaimedYieldUsd is > 0.005m)
                    details += $"\n  ◦ Unclaimed yield: {Signed(p.UnclaimedYieldUsd.Value)}";
            }
            else if (!p.Expired)
                details += "\nPnL: temporarily unavailable";

            if (p.Expired)
                details += "\nStatus: matured";
            positions.Add(new ExponentPosition { Section = "Farm", Market = name, Details = details, CachedSeconds = p.CachedAgeSeconds });
        }

        // Protected: tranche سینیور/جونیور
        foreach (var t in result.Tranches)
        {
            var side = t.Side == "senior" ? "Senior (protected)" : "Junior";
            var details = $"Tranche: {side}"
                + $"\nBalance: {t.LpBalance.ToString("0.######", CultureInfo.InvariantCulture)} {t.Symbol}"
                + $"\nValue: {Value(t.ValueUsd)}";
            if (t.Apy != null)
                details += $"\nAPY: {Num(t.Apy.Value * 100)}%";
            details += $"\nPnL: {Pnl(t.Pnl)}";
            positions.Add(new ExponentPosition { Section = "Protected", Market = t.Symbol, Details = details, CachedSeconds = result.ExtrasCachedAgeSeconds });
        }

        // Liquidity: پوزیشن LP (رنج بر اساس APY)
        foreach (var l in result.Liquidity)
        {
            var range = $"{Num(l.LowerApyPct)}% – {Num(l.UpperApyPct)}%";
            var status = l.InRange switch { true => " (in range)", false => " (out of range)", _ => "" };
            var details = $"Range: {range}{status}";
            if (l.ValueUsd != null)
                details += $"\nValue: {Value(l.ValueUsd.Value)}";
            if (l.Pnl != null)
                details += $"\nPnL (est.): {Pnl(l.Pnl.Value)}";
            positions.Add(new ExponentPosition { Section = "Liquidity", Market = l.Name, Details = details, CachedSeconds = result.ExtrasCachedAgeSeconds });
        }

        return new ExponentResult { Positions = positions, ExtrasFailed = result.ExtrasFailed };
    }

    private static string Num(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    // مقدارهای خیلی کوچک (گرد‌شده به صفر) را «<$0.01» نشان می‌دهد تا صفر غلط‌انداز نباشد
    private static string Value(decimal usd) => usd > 0 && usd < 0.01m ? "<$0.01" : $"${Num(usd)}";

    private static string Pnl(decimal usd) => Math.Abs(usd) < 0.005m ? "$0.00" : Signed(usd);

    // -$154.14 / +$203.18
    private static string Signed(decimal value) =>
        $"{(value < 0 ? "-" : "+")}${Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture)}";
}

public class ExponentServiceResponse
{
    public List<ExponentServicePosition> Positions { get; set; } = new();
    public List<ExponentServiceTranche> Tranches { get; set; } = new();
    public List<ExponentServiceLiquidity> Liquidity { get; set; } = new();
    public bool ExtrasFailed { get; set; }
    public int? ExtrasCachedAgeSeconds { get; set; }
}

public class ExponentServicePosition
{
    public string Vault { get; set; } = "";
    public string? Name { get; set; }
    public decimal Yt { get; set; }
    public decimal ClaimableInterest { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Expired { get; set; }

    // از API رسمی Exponent (دلار)؛ اگر در دسترس نبود null است.
    public decimal? AvgCostBasis { get; set; }
    public decimal? CurrentValue { get; set; }
    public decimal? PriceAppreciation { get; set; }
    public decimal? ClaimedInterestUsd { get; set; }
    public decimal? RealizedPnl { get; set; }
    public decimal? UnclaimedYieldUsd { get; set; }
    public decimal? TotalPnl { get; set; }

    // null: همه‌چیز تازه بود؛ عدد: بخشی از داده از کش سرویس Node آمده.
    public int? CachedAgeSeconds { get; set; }
}

public class ExponentServiceTranche
{
    public string Side { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal LpBalance { get; set; }
    public decimal ValueUsd { get; set; }
    public decimal Pnl { get; set; }
    public decimal? Apy { get; set; }
}

public class ExponentServiceLiquidity
{
    public string Name { get; set; } = "";
    public decimal LowerApyPct { get; set; }
    public decimal UpperApyPct { get; set; }
    public bool? InRange { get; set; }
    public decimal? ValueUsd { get; set; }
    public decimal? Pnl { get; set; }
}
