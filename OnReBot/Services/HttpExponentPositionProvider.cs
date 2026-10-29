using System.Globalization;
using System.Text.Json;
using OnReBot.Config;
using OnReBot.Models;

namespace OnReBot.Services;

// پوزیشن YT را از سرویس Node (ExponentService) می‌خواند.
public class HttpExponentPositionProvider(HttpClient http, BotConfig config) : IExponentPositionProvider
{
    public async Task<List<ExponentPosition>> GetPositionsAsync(string wallet, CancellationToken ct)
    {
        var vaults = string.Join("&", config.ExponentMarkets.Select(m => $"vault={m.Vault}"));
        var url = $"{config.ExponentServiceUrl.TrimEnd('/')}/positions/{wallet}?{vaults}";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct), config.ExponentMarkets);
    }

    public static List<ExponentPosition> Parse(string json, List<ExponentMarketConfig> markets)
    {
        var result = JsonSerializer.Deserialize<ExponentServiceResponse>(json, Json.Options) ?? new();
        var positions = new List<ExponentPosition>();
        foreach (var p in result.Positions)
        {
            var name = markets.FirstOrDefault(m => m.Vault == p.Vault)?.Name ?? p.Vault;
            var details = $"YT balance: {p.Yt.ToString("N2", CultureInfo.InvariantCulture)}"
                + $"\nMaturity: {p.ExpiresAt.UtcDateTime:yyyy-MM-dd}"
                + $"\nClaimable interest: {p.ClaimableInterest.ToString("N2", CultureInfo.InvariantCulture)}";
            if (p.CurrentValue != null)
                details += $"\nCurrent value: ${p.CurrentValue.Value.ToString("N2", CultureInfo.InvariantCulture)}";
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
            positions.Add(new ExponentPosition { Market = name, Details = details, CachedSeconds = p.CachedAgeSeconds });
        }
        return positions;
    }

    // -$154.14 / +$203.18
    private static string Signed(decimal value) =>
        $"{(value < 0 ? "-" : "+")}${Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture)}";
}

public class ExponentServiceResponse
{
    public List<ExponentServicePosition> Positions { get; set; } = new();
}

public class ExponentServicePosition
{
    public string Vault { get; set; } = "";
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
