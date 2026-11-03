using System.Globalization;
using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

// ONyc <-> USDC prices per DEX from Jupiter's public quote API (no key needed). Jupiter can be limited to one DEX with the
// `dexes` parameter, so the price on each DEX is asked separately; the unrestricted "best route" quote is asked too (it can
// mix several DEXes). Only quotes are requested: nothing is executed and no wallet is involved.
// Quotes are the same for every user, so they are cached briefly and shared.
public class JupiterClient(HttpClient http)
{
    public const string Usdc = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v";
    public const string Onyc = "5Y8NV33Vv7WbnLfq3zBcKSdYPrk7g2KoiQoe7M2tcxp5";
    private const int UsdcDecimals = 6;
    private const int OnycDecimals = 9;
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    // The swap size used for the prices (in the token that is paid). Large enough for a realistic price, small enough that the
    // trade itself hardly moves it.
    public const decimal Amount = 100m;

    // DEXes that are always tried, on top of the ones the best route uses. A DEX with no direct ONyc/USDC route is simply left out.
    private static readonly string[] KnownDexes = { "Whirlpool", "Raydium CLMM" };

    // The keyless API answers 429 when many requests arrive at once, so they are spaced out (and retried once after a 429).
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(350);
    private readonly SemaphoreSlim _spacing = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    private readonly Dictionary<string, (DateTimeOffset FetchedAt, SwapRate? Rate)> _cache = new();
    private readonly object _lock = new();

    // Both directions for the standard amount (the swap-rate screen).
    public async Task<SwapRates> GetRatesAsync(CancellationToken ct)
    {
        var both = await Task.WhenAll(GetQuotesAsync(buy: true, Amount, ct), GetQuotesAsync(buy: false, Amount, ct));
        var rates = both.SelectMany(r => r).ToList();
        return new SwapRates { Rates = rates, Failed = rates.Count == 0 };
    }

    // One direction for any amount (the calculator): the best route plus every DEX on its own.
    public async Task<List<SwapRate>> GetQuotesAsync(bool buy, decimal amount, CancellationToken ct)
    {
        // First the unrestricted best route (also tells which DEXes it uses), then each DEX on its own.
        var best = await QuoteSafeAsync(buy, amount, null, ct);

        var dexes = KnownDexes.Concat(best?.Routes ?? new()).Distinct().ToList();
        var perDex = await Task.WhenAll(dexes.Select(dex => QuoteSafeAsync(buy, amount, dex, ct)));

        return new[] { best }.Concat(perDex).Where(r => r != null).Select(r => r!).ToList();
    }

    // null: no price (for example the DEX has no direct ONyc/USDC pool, or the request failed)
    private async Task<SwapRate?> QuoteSafeAsync(bool buy, decimal amount, string? dex, CancellationToken ct)
    {
        try
        {
            return await QuoteAsync(buy, amount, dex, ct);
        }
        catch (HttpRequestException ex) when (dex != null && ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return null;   // "no routes found" on this DEX alone is normal
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[Jupiter] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<SwapRate?> QuoteAsync(bool buy, decimal amount, string? dex, CancellationToken ct)
    {
        var key = $"{(buy ? "buy" : "sell")}:{amount}:{dex}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < CacheFor)
                return hit.Rate == null ? null : hit.Rate with { CachedSeconds = (int)(DateTimeOffset.UtcNow - hit.FetchedAt).TotalSeconds };
        }

        var (inMint, outMint, inDecimals, outDecimals) = buy ? (Usdc, Onyc, UsdcDecimals, OnycDecimals) : (Onyc, Usdc, OnycDecimals, UsdcDecimals);
        var raw = (long)Math.Truncate(amount * (decimal)Math.Pow(10, inDecimals));
        var url = $"https://api.jup.ag/swap/v1/quote?inputMint={inMint}&outputMint={outMint}&amount={raw}&slippageBps=50";
        if (dex != null)
            url += $"&dexes={Uri.EscapeDataString(dex)}";

        using var response = await GetSpacedAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var rate = ParseQuote(await response.Content.ReadAsStringAsync(ct), buy, amount, outDecimals, dex);

        lock (_lock)
        {
            // Calculator amounts make the key space open-ended, so old entries are dropped as new ones come in.
            foreach (var old in _cache.Where(e => DateTimeOffset.UtcNow - e.Value.FetchedAt >= CacheFor).Select(e => e.Key).ToList())
                _cache.Remove(old);
            _cache[key] = (DateTimeOffset.UtcNow, rate);
        }
        return rate;
    }

    private async Task<HttpResponseMessage> GetSpacedAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _spacing.WaitAsync(ct);
            try
            {
                var wait = MinGap - (DateTimeOffset.UtcNow - _lastRequest);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);
                _lastRequest = DateTimeOffset.UtcNow;
            }
            finally
            {
                _spacing.Release();
            }

            var response = await http.GetAsync(url, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests || attempt >= 1)
                return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(1.2), ct);
        }
    }

    public static SwapRate ParseQuote(string json, bool buy, decimal amountIn, int outDecimals, string? dex = null)
    {
        var quote = JsonSerializer.Deserialize<JupiterQuote>(json, Json.Options) ?? throw new InvalidOperationException("empty quote");
        var amountOut = decimal.Parse(quote.OutAmount, CultureInfo.InvariantCulture) / (decimal)Math.Pow(10, outDecimals);
        if (amountOut <= 0)
            throw new InvalidOperationException("quote has no output");

        var impact = decimal.TryParse(quote.PriceImpactPct, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed * 100 : 0m;
        var routes = quote.RoutePlan.Select(r => r.SwapInfo.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();

        // USD per ONyc, whichever direction was asked
        var usdPerOnyc = buy ? amountIn / amountOut : amountOut / amountIn;
        return new SwapRate { Buy = buy, Dex = dex, Source = dex == null ? "Jupiter" : null, AmountIn = amountIn, AmountOut = amountOut, UsdPerOnyc = usdPerOnyc, PriceImpactPct = impact, Routes = routes };
    }
}

public class JupiterQuote
{
    public string OutAmount { get; set; } = "0";
    public string? PriceImpactPct { get; set; }
    public List<JupiterRoute> RoutePlan { get; set; } = new();
}

public class JupiterRoute
{
    public JupiterSwapInfo SwapInfo { get; set; } = new();
}

public class JupiterSwapInfo
{
    public string Label { get; set; } = "";
}
