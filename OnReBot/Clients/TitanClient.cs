using System.Globalization;
using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

// ONyc <-> USDC price from Titan's Developers Portal API (https://portal.api.titan.exchange, needs a free API key).
// Titan is an aggregator too: it returns its best route over many DEXes. Only quotes are requested: nothing is executed.
// The key is read from the TITAN_API_KEY environment variable and never stored in the project. Without a key this client
// returns nothing and the bot simply shows the other sources.
public class TitanClient(HttpClient http, string? apiKey)
{
    // The quote endpoint wants a wallet address, but no transaction is built (simulate=false), so a placeholder is used and
    // no user data is sent: this is the System Program address.
    private const string PlaceholderWallet = "11111111111111111111111111111111";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    // The free plan allows 1 request per second, so requests are spaced out.
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(1100);

    private readonly Dictionary<string, (DateTimeOffset FetchedAt, SwapRate Rate)> _cache = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _spacing = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public bool Enabled => !string.IsNullOrWhiteSpace(apiKey);

    // Both directions for the standard amount (the swap-rate screen).
    public async Task<List<SwapRate>> GetRatesAsync(CancellationToken ct)
    {
        var buy = await GetQuotesAsync(buy: true, JupiterClient.Amount, ct);
        var sell = await GetQuotesAsync(buy: false, JupiterClient.Amount, ct);
        return buy.Concat(sell).ToList();
    }

    // One direction for any amount (the calculator): at most one quote, none when no key is set or the request fails.
    public async Task<List<SwapRate>> GetQuotesAsync(bool buy, decimal amount, CancellationToken ct)
    {
        if (!Enabled)
            return new();

        var rate = await QuoteSafeAsync(buy, amount, ct);
        return rate == null ? new() : new() { rate };
    }

    private async Task<SwapRate?> QuoteSafeAsync(bool buy, decimal amount, CancellationToken ct)
    {
        try
        {
            return await QuoteAsync(buy, amount, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[Titan] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<SwapRate> QuoteAsync(bool buy, decimal amount, CancellationToken ct)
    {
        var key = $"{(buy ? "buy" : "sell")}:{amount}";
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < CacheFor)
                return hit.Rate with { CachedSeconds = (int)(DateTimeOffset.UtcNow - hit.FetchedAt).TotalSeconds };
        }

        var (inMint, outMint, inDecimals, outDecimals) = buy
            ? (JupiterClient.Usdc, JupiterClient.Onyc, 6, 9)
            : (JupiterClient.Onyc, JupiterClient.Usdc, 9, 6);
        var raw = (long)Math.Truncate(amount * (decimal)Math.Pow(10, inDecimals));
        var url = $"https://portal.api.titan.exchange/api/v1/quote/swap?inputMint={inMint}&outputMint={outMint}&amount={raw}"
            + $"&slippageBps=50&userPublicKey={PlaceholderWallet}&simulate=false";

        await _spacing.WaitAsync(ct);
        try
        {
            var wait = MinGap - (DateTimeOffset.UtcNow - _lastRequest);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);
            _lastRequest = DateTimeOffset.UtcNow;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("Accept", "application/json");
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var rate = ParseQuote(await response.Content.ReadAsStringAsync(ct), buy, amount, outDecimals);

            lock (_lock)
            {
                foreach (var old in _cache.Where(e => DateTimeOffset.UtcNow - e.Value.FetchedAt >= CacheFor).Select(e => e.Key).ToList())
                    _cache.Remove(old);
                _cache[key] = (DateTimeOffset.UtcNow, rate);
            }
            return rate;
        }
        finally
        {
            _spacing.Release();
        }
    }

    // The response has a "quotes" object keyed by provider. Titan's own recommendation (metadata.ExpectedWinner) is used,
    // otherwise the quote with the largest output.
    public static SwapRate ParseQuote(string json, bool buy, decimal amountIn, int outDecimals)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("no quotes");

        string? winner = null;
        if (doc.RootElement.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("ExpectedWinner", out var w) && w.ValueKind == JsonValueKind.String)
            winner = w.GetString();

        JsonElement? chosen = null;
        decimal chosenOut = 0;
        foreach (var provider in quotes.EnumerateObject())
        {
            var outAmount = provider.Value.GetProperty("outAmount").GetDecimal();
            if (provider.Name == winner)
            {
                chosen = provider.Value;
                chosenOut = outAmount;
                break;
            }
            if (outAmount > chosenOut)
            {
                chosen = provider.Value;
                chosenOut = outAmount;
            }
        }
        if (chosen == null || chosenOut <= 0)
            throw new InvalidOperationException("quote has no output");

        var amountOut = chosenOut / (decimal)Math.Pow(10, outDecimals);
        var routes = new List<string>();
        if (chosen.Value.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            routes = steps.EnumerateArray()
                .Select(s => s.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "")
                .Where(l => l != "").Distinct().ToList();

        var usdPerOnyc = buy ? amountIn / amountOut : amountOut / amountIn;
        return new SwapRate { Buy = buy, Source = "Titan", AmountIn = amountIn, AmountOut = amountOut, UsdPerOnyc = usdPerOnyc, Routes = routes };
    }
}
